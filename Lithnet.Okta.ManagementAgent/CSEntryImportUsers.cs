using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lithnet.MetadirectoryServices;
using Microsoft.MetadirectoryServices;
using NLog;
using Okta.Sdk;

namespace Lithnet.Okta.ManagementAgent
{
    public class CSEntryImportUsers
    {
        private static long userHighestTicks = 0;

        private static Logger logger = LogManager.GetCurrentClassLogger();

        internal static IEnumerable<Watermark> GetCSEntryChanges(bool inDelta, WatermarkKeyedCollection importState, SchemaType schemaType, CancellationToken cancellationToken, BlockingCollection<CSEntryChange> importItems, IOktaClient client)
        {
            ParallelOptions options = new ParallelOptions { CancellationToken = cancellationToken };

            if (Debugger.IsAttached)
            {
                options.MaxDegreeOfParallelism = 1;
            }

            object syncObject = new object();
            userHighestTicks = 0;

            IEnumerable<IUser> users = CSEntryImportUsers.GetUserEnumerable(inDelta, importState, client);

            Parallel.ForEach<IUser>(users, options, (user) =>
            {
                try
                {
                    if (user.LastUpdated.HasValue)
                    {
                        lock (syncObject)
                        {
                            CSEntryImportUsers.userHighestTicks = Math.Max(CSEntryImportUsers.userHighestTicks, user.LastUpdated.Value.Ticks);
                        }
                    }
                    
                    CSEntryChange c = CSEntryImportUsers.UserToCSEntryChange(inDelta, schemaType, user);
                    if (c != null)
                    {
                        importItems.Add(c, cancellationToken);
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
                    importItems.Add(csentry, cancellationToken);
                }

                options.CancellationToken.ThrowIfCancellationRequested();
            });

            if (CSEntryImportUsers.userHighestTicks <= 0)
            {
                yield break;
            }

            string wmv = CSEntryImportUsers.userHighestTicks.ToString();
            yield return new Watermark("users", wmv, "DateTime");
        }
        
        private static CSEntryChange UserToCSEntryChange(bool inDelta, SchemaType schemaType, IUser user)
        {
            Resource profile = user.GetProperty<Resource>("profile");
            string login = profile.GetProperty<string>("login");
            logger.Trace($"Creating CSEntryChange for {user.Id}/{login}");

            ObjectModificationType modType = CSEntryImportUsers.GetObjectModificationType(user, inDelta);

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

                    object value = user.GetProperty<object>(type.Name);

                    if (value != null)
                    {
                        c.AttributeChanges.Add(AttributeChange.CreateAttributeAdd(type.Name, TypeConverter.ConvertData(value, type.DataType)));
                        continue;
                    }

                    value = profile.GetProperty<object>(type.Name);
                    if (value != null)
                    {
                        c.AttributeChanges.Add(AttributeChange.CreateAttributeAdd(type.Name, TypeConverter.ConvertData(value, type.DataType)));
                        continue;
                    }
                }
            }

            return c;
        }

        private static IEnumerable<IUser> GetUserEnumerable(bool inDelta, WatermarkKeyedCollection importState, IOktaClient client)
        {
            IEnumerable<IUser> users;

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

                users = client.Users.ListUsers(null, null, null, filter).ToEnumerable();
            }
            else
            {
                users = client.Users.ListUsers().ToEnumerable();
            }

            return users;
        }

        private static ObjectModificationType GetObjectModificationType(IUser user, bool inDelta)
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
    }
}
