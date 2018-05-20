using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Lithnet.Ecma2Framework;
using Lithnet.MetadirectoryServices;
using Microsoft.MetadirectoryServices;
using NLog;
using Okta.Sdk;

namespace Lithnet.Okta.ManagementAgent
{
    internal  class UserImportProvider : IObjectImportProvider
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();

        public void GetCSEntryChanges(ImportContext context, SchemaType type)
        {
            ParallelOptions options = new ParallelOptions { CancellationToken = context.CancellationTokenSource.Token };

            if (Debugger.IsAttached)
            {
                options.MaxDegreeOfParallelism = 1;
            }

            object syncObject = new object();
            long userHighestTicks = 0;

            IAsyncEnumerable<IUser> users = this.GetUserEnumerable(context.InDelta, context.IncomingWatermark, ((OktaConnectionContext)context.ConnectionContext).Client);

            Parallel.ForEach<IUser>(users.ToEnumerable(), options, (user) =>
            {
                try
                {
                    if (user.LastUpdated.HasValue)
                    {
                        lock (syncObject)
                        {
                            userHighestTicks = Math.Max(userHighestTicks, user.LastUpdated.Value.Ticks);
                        }
                    }

                    CSEntryChange c = this.UserToCSEntryChange(context.InDelta, type, user);
                    if (c != null)
                    {
                        context.ImportItems.Add(c, context.CancellationTokenSource.Token);
                    }
                }
                catch (Exception ex)
                {
                    logger.Error(ex);
                    CSEntryChange csentry = CSEntryChange.Create();
                    csentry.DN = user.Id;
                    csentry.ErrorCodeImport = MAImportError.ImportErrorCustomContinueRun;
                    csentry.ErrorDetail = ex.StackTrace;
                    csentry.ErrorName = ex.Message;
                    context.ImportItems.Add(csentry, context.CancellationTokenSource.Token);
                }

                options.CancellationToken.ThrowIfCancellationRequested();
            });

            string wmv;

            if (userHighestTicks <= 0)
            {
                wmv = context.IncomingWatermark["users"].Value;
            }
            else
            {
                wmv = userHighestTicks.ToString();
            }

            context.OutgoingWatermark.Add(new Watermark("users", wmv, "DateTime"));
        }

        private CSEntryChange UserToCSEntryChange(bool inDelta, SchemaType schemaType, IUser user)
        {
            Resource profile = user.GetProperty<Resource>("profile");
            string login = profile.GetProperty<string>("login");
            logger.Trace($"Creating CSEntryChange for {user.Id}/{login}");

            ObjectModificationType modType = this.GetObjectModificationType(user, inDelta);

            if (modType == ObjectModificationType.None)
            {
                return null;
            }

            CSEntryChange c = CSEntryChange.Create();
            c.ObjectType = "user";
            c.ObjectModificationType = modType;
            c.AnchorAttributes.Add(AnchorAttribute.Create("id", user.Id));
            c.DN = user.Id;

            if (modType != ObjectModificationType.Delete)
            {
                foreach (SchemaAttribute type in schemaType.Attributes)
                {
                    if (type.Name == "provider.name")
                    {
                        c.AttributeChanges.Add(AttributeChange.CreateAttributeAdd(type.Name, TypeConverter.ConvertData(user.Credentials.Provider.Name, type.DataType)));
                        continue;
                    }
                    else if (type.Name == "provider.type")
                    {
                        c.AttributeChanges.Add(AttributeChange.CreateAttributeAdd(type.Name, TypeConverter.ConvertData(user.Credentials.Provider.Type.Value, type.DataType)));
                        continue;
                    }
                    else if (type.Name == "suspended")
                    {
                        c.AttributeChanges.Add(AttributeChange.CreateAttributeAdd(type.Name, user.Status == UserStatus.Suspended));
                        continue;
                    }

                    object value = user.GetProperty<object>(type.Name) ?? profile.GetProperty<object>(type.Name);

                    if (value != null)
                    {
                        if (value is IList list)
                        {
                            IList<object> values = new List<object>();

                            foreach (var item in list)
                            {
                                values.Add(TypeConverter.ConvertData(item, type.DataType));
                            }

                            c.AttributeChanges.Add(AttributeChange.CreateAttributeAdd(type.Name, values));
                        }
                        else
                        {
                            c.AttributeChanges.Add(AttributeChange.CreateAttributeAdd(type.Name, TypeConverter.ConvertData(value, type.DataType)));
                        }
                    }
                }
            }

            return c;
        }

        private IAsyncEnumerable<IUser> GetUserEnumerable(bool inDelta, WatermarkKeyedCollection importState, IOktaClient client)
        {
            IAsyncEnumerable<IUser> users;

            if (inDelta)
            {
                if (importState == null)
                {
                    throw new WarningNoWatermarkException("No watermark was available to perform a delta import");
                }

                if (!importState.Contains("users"))
                {
                    throw new WarningNoWatermarkException("No watermark was available to perform a delta import for the user object type");
                }

                string value = importState["users"].Value;
                long ticks = long.Parse(value);
                DateTime dt = new DateTime(ticks);

                string filter = $"lastUpdated gt \"{dt.ToSmartString()}Z\"";

                users = client.Users.ListUsers(null, null, 200, filter);
            }
            else
            {
                users = client.Users.ListUsers(null, null, 200);
            }

            return users;
        }

        private ObjectModificationType GetObjectModificationType(IUser user, bool inDelta)
        {
            if (!inDelta)
            {
                if ((user.Status?.Value == UserStatus.Deprovisioned ||
                     user.TransitioningToStatus?.Value == UserStatus.Deprovisioned) &&
                    user.TransitioningToStatus?.Value != UserStatus.Provisioned)
                {

                    logger.Trace($"Discarding {user.Id} as status is deprovisioned");
                    return ObjectModificationType.None;
                }

                return ObjectModificationType.Add;
            }

            if ((user.Status?.Value == UserStatus.Deprovisioned ||
                 user.TransitioningToStatus?.Value == UserStatus.Deprovisioned) &&
                user.TransitioningToStatus?.Value != UserStatus.Provisioned)
            {
                return ObjectModificationType.Delete;
            }

            return ObjectModificationType.Replace;
        }

        public bool CanImport(SchemaType type)
        {
            return type.Name == "user";
        }
    }
}
