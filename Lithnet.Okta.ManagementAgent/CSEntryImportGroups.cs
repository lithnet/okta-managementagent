using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Lithnet.MetadirectoryServices;
using Microsoft.MetadirectoryServices;
using NLog;
using Okta.Sdk;

namespace Lithnet.Okta.ManagementAgent
{
    internal static class CSEntryImportGroups
    {
        private const string GroupUpdateKey = "group";
        private const string GroupMemberUpdateKey = "group-member";

        private static long groupUpdateHighestTicks = 0;
        private static long groupMemberUpdateHighestTicks = 0;


        private static DateTime? lastGroupUpdateHighWatermark;
        private static DateTime? lastGroupMemberUpdateHighWatermark;


        private static Logger logger = LogManager.GetCurrentClassLogger();

        internal static void GetCSEntryChanges(SchemaType schemaType, ImportContext context)
        {
            ParallelOptions options = new ParallelOptions { CancellationToken = context.CancellationTokenSource.Token };

            if (Debugger.IsAttached)
            {
                options.MaxDegreeOfParallelism = 1;
            }

            object syncObject = new object();
            CSEntryImportGroups.groupUpdateHighestTicks = 0;
            CSEntryImportGroups.groupMemberUpdateHighestTicks = 0;

            IAsyncEnumerable<IGroup> groups = CSEntryImportGroups.GetGroupEnumerable(context.InDelta, context.ConfigParameters, context.IncomingWatermark, ((OktaConnectionContext)context.ConnectionContext).Client);
            
            Parallel.ForEach<IGroup>(groups.ToEnumerable(), options, (group) =>
            {
                try
                {
                    if (group.LastUpdated.HasValue)
                    {
                        lock (syncObject)
                        {
                            CSEntryImportGroups.groupUpdateHighestTicks = Math.Max(CSEntryImportGroups.groupUpdateHighestTicks, group.LastUpdated.Value.Ticks);
                            CSEntryImportGroups.groupMemberUpdateHighestTicks = Math.Max(CSEntryImportGroups.groupMemberUpdateHighestTicks, group.LastMembershipUpdated.Value.Ticks);
                        }
                    }

                    CSEntryChange c = CSEntryImportGroups.GroupToCSEntryChange(context.InDelta, schemaType, group);
                    if (c != null)
                    {
                        context.ImportItems.Add(c, context.CancellationTokenSource.Token);
                    }
                }
                catch (Exception ex)
                {
                    logger.Error(ex);
                    CSEntryChange csentry = CSEntryChange.Create();
                    csentry.DN = group.Id;
                    csentry.ErrorCodeImport = MAImportError.ImportErrorCustomContinueRun;
                    csentry.ErrorDetail = ex.StackTrace;
                    csentry.ErrorName = ex.Message;
                    context.ImportItems.Add(csentry, context.CancellationTokenSource.Token);
                }

                options.CancellationToken.ThrowIfCancellationRequested();
            });

            string wmv;

            if (CSEntryImportGroups.groupUpdateHighestTicks <= 0)
            {
                wmv = context.IncomingWatermark[GroupUpdateKey].Value;
            }
            else
            {
                wmv = CSEntryImportGroups.groupUpdateHighestTicks.ToString();
            }

            context.OutgoingWatermark.Add(new Watermark(GroupUpdateKey, wmv, "DateTime"));

            if (CSEntryImportGroups.groupMemberUpdateHighestTicks <= 0)
            {
                wmv = context.IncomingWatermark[GroupMemberUpdateKey].Value;
            }
            else
            {
                wmv = CSEntryImportGroups.groupMemberUpdateHighestTicks.ToString();
            }

            context.OutgoingWatermark.Add(new Watermark(GroupMemberUpdateKey, wmv, "DateTime"));
        }

        private static CSEntryChange GroupToCSEntryChange(bool inDelta, SchemaType schemaType, IGroup group)
        {
            Resource profile = group.GetProperty<Resource>("profile");
            logger.Trace($"Creating CSEntryChange for group {group.Id}");

            ObjectModificationType modType = CSEntryImportGroups.GetObjectModificationType(group, inDelta);

            if (modType == ObjectModificationType.None)
            {
                return null;
            }

            CSEntryChange c = CSEntryChange.Create();
            c.ObjectType = "group";
            c.ObjectModificationType = modType;
            c.AnchorAttributes.Add(AnchorAttribute.Create("id", group.Id));
            c.DN = group.Id;

            if (modType == ObjectModificationType.Delete)
            {
                return c;
            }

            foreach (SchemaAttribute type in schemaType.Attributes)
            {
                if (type.Name == "member")
                {
                    IList<object> members = new List<object>();
                  
                    group.UsersSkinny.ForEach(u =>
                    {
                        members.Add(u.Id);
                    });

                    if (modType == ObjectModificationType.Update)
                    {
                        if (members.Count == 0)
                        {
                            c.AttributeChanges.Add(AttributeChange.CreateAttributeDelete(type.Name));
                        }
                        else
                        {
                            c.AttributeChanges.Add(AttributeChange.CreateAttributeReplace(type.Name, members));
                        }
                    }
                    else
                    {
                        c.AttributeChanges.Add(AttributeChange.CreateAttributeAdd(type.Name, members));
                    }

                    continue;
                }

                object value = group.GetProperty<object>(type.Name) ?? profile.GetProperty<object>(type.Name);

                if (modType == ObjectModificationType.Update)
                {
                    if (value == null)
                    {
                        c.AttributeChanges.Add(AttributeChange.CreateAttributeDelete(type.Name));
                    }
                    else
                    {
                        c.AttributeChanges.Add(AttributeChange.CreateAttributeReplace(type.Name, TypeConverter.ConvertData(value, type.DataType)));
                    }
                }
                else
                {
                    if (value != null)
                    {
                        c.AttributeChanges.Add(AttributeChange.CreateAttributeAdd(type.Name, TypeConverter.ConvertData(value, type.DataType)));
                    }
                }

                continue;
            }

            return c;
        }

        private static DateTime GetLastHighWatermarkGroup(WatermarkKeyedCollection importState)
        {
            return GetLastHighWatermark(importState, GroupUpdateKey);
        }

        private static DateTime GetLastHighWatermarkGroupMember(WatermarkKeyedCollection importState)
        {
            return GetLastHighWatermark(importState, GroupMemberUpdateKey);
        }



        private static DateTime GetLastHighWatermark(WatermarkKeyedCollection importState, string key)
        {
            if (importState == null)
            {
                throw new WarningNoWatermarkException("No watermark was available to perform a delta import");
            }

            if (!importState.Contains(key))
            {
                throw new WarningNoWatermarkException("No watermark was available to perform a delta import for the group object type");
            }

            string value = importState[key].Value;
            long ticks = long.Parse(value);
            return new DateTime(ticks);
        }

        private static IAsyncEnumerable<IGroup> GetGroupEnumerable(bool inDelta, MAConfigParameters configParameters, WatermarkKeyedCollection importState, IOktaClient client)
        {
            List<string> filterConditions = new List<string>();

            if (configParameters.IncludeBuiltInGroups)
            {
                filterConditions.Add("(type eq \"BUILT_IN\")");
            }

            if (configParameters.IncludeAppGroups)
            {
                filterConditions.Add("(type eq \"APP_GROUP\")");
            }

            filterConditions.Add("(type eq \"OKTA_GROUP\")");

            string filter = string.Join(" OR ", filterConditions);

            if (inDelta)
            {
                CSEntryImportGroups.lastGroupUpdateHighWatermark = GetLastHighWatermarkGroup(importState);
                CSEntryImportGroups.lastGroupMemberUpdateHighWatermark = GetLastHighWatermarkGroupMember(importState);

                filter = $"({filter}) AND (lastUpdated gt \"{lastGroupUpdateHighWatermark.ToSmartString()}Z\" or lastMembershipUpdated gt \"{lastGroupMemberUpdateHighWatermark.ToSmartString()}Z\")";
            }

            return client.Groups.ListGroups(null, filter);
        }

        private static ObjectModificationType GetObjectModificationType(IGroup group, bool inDelta)
        {
            if (!inDelta)
            {
                return ObjectModificationType.Add;
            }

            if (group.Created > CSEntryImportGroups.lastGroupUpdateHighWatermark)
            {
                return ObjectModificationType.Add;
            }

            return ObjectModificationType.Update;
        }
    }
}
