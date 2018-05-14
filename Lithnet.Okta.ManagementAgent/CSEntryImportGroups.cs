using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
    public static class CSEntryImportGroups
    {
        private static long groupHighestTicks = 0;

        private static DateTime? lastHighWatermark;

        private static Logger logger = LogManager.GetCurrentClassLogger();

        internal static IEnumerable<Watermark> GetCSEntryChanges(bool inDelta, KeyedCollection<string, ConfigParameter> configParameters, WatermarkKeyedCollection importState, SchemaType schemaType, CancellationToken cancellationToken, BlockingCollection<CSEntryChange> importItems, IOktaClient client)
        {
            ParallelOptions options = new ParallelOptions { CancellationToken = cancellationToken };

            if (Debugger.IsAttached)
            {
                options.MaxDegreeOfParallelism = 1;
            }

            object syncObject = new object();
            CSEntryImportGroups.groupHighestTicks = 0;

            IAsyncEnumerable<IGroup> groups = CSEntryImportGroups.GetGroupEnumerable(inDelta, configParameters, importState, client);
            
            //groups.ForEach(group =>
            Parallel.ForEach<IGroup>(groups.ToEnumerable(), options, (group) =>
            {
                try
                {
                    if (group.LastUpdated.HasValue)
                    {
                        lock (syncObject)
                        {
                            CSEntryImportGroups.groupHighestTicks = Math.Max(CSEntryImportGroups.groupHighestTicks, group.LastUpdated.Value.Ticks);
                            CSEntryImportGroups.groupHighestTicks = Math.Max(CSEntryImportGroups.groupHighestTicks, group.LastMembershipUpdated.Value.Ticks);
                        }
                    }

                    CSEntryChange c = CSEntryImportGroups.GroupToCSEntryChange(inDelta, schemaType, group);
                    if (c != null)
                    {
                        importItems.Add(c, cancellationToken);
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
                    importItems.Add(csentry, cancellationToken);
                }

                options.CancellationToken.ThrowIfCancellationRequested();
            });

            string wmv;

            if (CSEntryImportGroups.groupHighestTicks <= 0)
            {
                wmv = importState["groups"].Value;
            }
            else
            {
                wmv = CSEntryImportGroups.groupHighestTicks.ToString();
            }
            
            yield return new Watermark("groups", wmv, "DateTime");
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

        private static DateTime GetLastHighWatermark(WatermarkKeyedCollection importState)
        {
            if (importState == null)
            {
                throw new WarningNoWatermarkException("No watermark was available to perform a delta import");
            }

            if (!importState.Contains("groups"))
            {
                throw new WarningNoWatermarkException("No watermark was available to perform a delta import for the group object type");
            }

            string value = importState["groups"].Value;
            long ticks = long.Parse(value);
            DateTime dt = new DateTime(ticks);

            return dt;
        }

        private static IAsyncEnumerable<IGroup> GetGroupEnumerable(bool inDelta, KeyedCollection<string, ConfigParameter> configParameters, WatermarkKeyedCollection importState, IOktaClient client)
        {
            IAsyncEnumerable<IGroup> groups;

            List<string> filterConditions = new List<string>();

            if (configParameters[Ecma2.ParameterNameIncludeBuiltInGroups].Value == "1")
            {
                filterConditions.Add("(type eq \"BUILT_IN\")");
            }

            if (configParameters[Ecma2.ParameterNameIncludeAppGroups].Value == "1")
            {
                filterConditions.Add("(type eq \"APP_GROUP\")");
            }

            filterConditions.Add("(type eq \"OKTA_GROUP\")");

            string filter = string.Join(" OR ", filterConditions);

            if (inDelta)
            {
                CSEntryImportGroups.lastHighWatermark = GetLastHighWatermark(importState);
                filter = $"({filter}) AND (lastUpdated gt \"{CSEntryImportGroups.lastHighWatermark.ToSmartString()}Z\" or lastMembershipUpdated gt \"{CSEntryImportGroups.lastHighWatermark.ToSmartString()}Z\")";
            }

            groups = client.Groups.ListGroups(null, filter);

            return groups;
        }

        private static ObjectModificationType GetObjectModificationType(IGroup group, bool inDelta)
        {
            if (!inDelta)
            {
                return ObjectModificationType.Add;
            }

            if (group.Created > CSEntryImportGroups.lastHighWatermark)
            {
                return ObjectModificationType.Add;
            }

            return ObjectModificationType.Update;
        }
    }
}
