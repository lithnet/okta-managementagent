using System;
using System.Collections.Generic;
using Lithnet.Ecma2Framework;
using Microsoft.MetadirectoryServices;
using NLog;
using Okta.Sdk;
using HttpRequest = Okta.Sdk.Internal.HttpRequest;

namespace Lithnet.Okta.ManagementAgent
{
    internal class SchemaProvider : ISchemaProvider
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();

        public Schema GetMmsSchema(SchemaContext context)
        {
            IOktaClient client = ((OktaConnectionContext)context.ConnectionContext).Client;

            Schema mmsSchema = new Schema();
            SchemaType mmsType = SchemaProvider.GetSchemaTypeUser(client);
            mmsSchema.Types.Add(mmsType);

            mmsType = SchemaProvider.GetSchemaTypeGroup(client);
            mmsSchema.Types.Add(mmsType);

            return mmsSchema;
        }

        private static SchemaType GetSchemaTypeGroup(IOktaClient client)
        {
            SchemaType mmsType = SchemaType.Create("group", true);
            SchemaAttribute mmsAttribute = SchemaAttribute.CreateAnchorAttribute("id", AttributeType.String, AttributeOperation.ImportOnly);
            mmsType.Attributes.Add(mmsAttribute);

            mmsAttribute = SchemaAttribute.CreateSingleValuedAttribute("created", AttributeType.String, AttributeOperation.ImportOnly);
            mmsType.Attributes.Add(mmsAttribute);

            mmsAttribute = SchemaAttribute.CreateSingleValuedAttribute("lastUpdated", AttributeType.String, AttributeOperation.ImportOnly);
            mmsType.Attributes.Add(mmsAttribute);

            mmsAttribute = SchemaAttribute.CreateSingleValuedAttribute("lastMembershipUpdated", AttributeType.String, AttributeOperation.ImportOnly);
            mmsType.Attributes.Add(mmsAttribute);

            mmsAttribute = SchemaAttribute.CreateSingleValuedAttribute("type", AttributeType.String, AttributeOperation.ImportOnly);
            mmsType.Attributes.Add(mmsAttribute);

            mmsAttribute = SchemaAttribute.CreateSingleValuedAttribute("name", AttributeType.String, AttributeOperation.ImportExport);
            mmsType.Attributes.Add(mmsAttribute);

            mmsAttribute = SchemaAttribute.CreateSingleValuedAttribute("description", AttributeType.String, AttributeOperation.ImportExport);
            mmsType.Attributes.Add(mmsAttribute);

            mmsAttribute = SchemaAttribute.CreateMultiValuedAttribute("member", AttributeType.Reference, AttributeOperation.ImportExport);
            mmsType.Attributes.Add(mmsAttribute);

            return mmsType;
        }
        
        private static SchemaType GetSchemaTypeUser(IOktaClient client)
        {
            SchemaType mmsType = SchemaType.Create("user", true);
            SchemaAttribute mmsAttribute = SchemaAttribute.CreateAnchorAttribute("id", AttributeType.String, AttributeOperation.ImportOnly);
            mmsType.Attributes.Add(mmsAttribute);

            mmsAttribute = SchemaAttribute.CreateSingleValuedAttribute("status", AttributeType.String);
            mmsType.Attributes.Add(mmsAttribute);

            mmsAttribute = SchemaAttribute.CreateSingleValuedAttribute("created", AttributeType.String, AttributeOperation.ImportOnly);
            mmsType.Attributes.Add(mmsAttribute);

            mmsAttribute = SchemaAttribute.CreateSingleValuedAttribute("activated", AttributeType.String, AttributeOperation.ImportOnly);
            mmsType.Attributes.Add(mmsAttribute);

            mmsAttribute = SchemaAttribute.CreateSingleValuedAttribute("statusChanged", AttributeType.String, AttributeOperation.ImportOnly);
            mmsType.Attributes.Add(mmsAttribute);

            mmsAttribute = SchemaAttribute.CreateSingleValuedAttribute("lastLogin", AttributeType.String, AttributeOperation.ImportOnly);
            mmsType.Attributes.Add(mmsAttribute);

            mmsAttribute = SchemaAttribute.CreateSingleValuedAttribute("lastUpdated", AttributeType.String, AttributeOperation.ImportOnly);
            mmsType.Attributes.Add(mmsAttribute);

            mmsAttribute = SchemaAttribute.CreateSingleValuedAttribute("passwordChanged", AttributeType.String, AttributeOperation.ImportOnly);
            mmsType.Attributes.Add(mmsAttribute);

            mmsAttribute = SchemaAttribute.CreateSingleValuedAttribute("provider.type", AttributeType.String, AttributeOperation.ImportOnly);
            mmsType.Attributes.Add(mmsAttribute);

            mmsAttribute = SchemaAttribute.CreateSingleValuedAttribute("provider.name", AttributeType.String, AttributeOperation.ImportOnly);
            mmsType.Attributes.Add(mmsAttribute);

            mmsAttribute = SchemaAttribute.CreateSingleValuedAttribute("suspended", AttributeType.Boolean, AttributeOperation.ImportExport);
            mmsType.Attributes.Add(mmsAttribute);
            
            foreach (SchemaAttribute a in SchemaProvider.GetSchemaJson(client))
            {
                mmsType.Attributes.Add(a);
            }

            return mmsType;
        }

        public static IEnumerable<SchemaAttribute> GetSchemaJson(IOktaClient client)
        {
            Resource result = client.GetAsync<Resource>(
                new HttpRequest
                {
                    Uri = "/api/v1/meta/schemas/user/default",
                }).Result;


            IDictionary<string, object> definitions = result["definitions"] as IDictionary<string, object>;

            foreach (SchemaAttribute schemaAttribute in SchemaProvider.GetAttributesFromDefinition(definitions, "base"))
            {
                yield return schemaAttribute;
            }

            foreach (SchemaAttribute schemaAttribute in SchemaProvider.GetAttributesFromDefinition(definitions, "custom"))
            {
                yield return schemaAttribute;
            }
        }

        private static IEnumerable<SchemaAttribute> GetAttributesFromDefinition(IDictionary<string, object> definitions, string definitionName)
        {
            if (!definitions.ContainsKey(definitionName))
            {
                logger.Error($"The definition for type {definitionName} was not found in the response");
                yield break;
            }

            if (!(definitions[definitionName] is IDictionary<string, object> definitionObject))
            {
                logger.Info($"The definition for type {definitionName} was null");
                yield break;
            }

            if (!definitionObject.ContainsKey("properties"))
            {
                logger.Error($"The definition for type {definitionName} was did not contain any properties");
                yield break;
            }

            if (!(definitionObject["properties"] is IDictionary<string, object> properties))
            {
                logger.Info($"The properties definition for {definitionName} were missing");
                yield break;
            }

            foreach (KeyValuePair<string, object> property in properties)
            {
                string name = property.Key;

                if (!(property.Value is IDictionary<string, object> values))
                {
                    logger.Warn($"Missing value set for property {name}");
                    continue;
                }

                AttributeOperation operation = SchemaProvider.GetAttributeOperationFromMutability(values["mutability"].ToString());

                bool ismultivalued = SchemaProvider.IsMultivalued(values);
                AttributeType type = SchemaProvider.GetTypeForAttribute(values, ismultivalued);

                if (name == "managerId")
                {
                    type = AttributeType.Reference;
                }

                logger.Info($"Got attribute {name} of type {type} and is mv {ismultivalued}");

                if (ismultivalued)
                {
                    yield return SchemaAttribute.CreateMultiValuedAttribute(name, type, operation);
                }
                else
                {
                    yield return SchemaAttribute.CreateSingleValuedAttribute(name, type, operation);
                }
            }
        }

        private static bool IsMultivalued(IDictionary<string, object> values)
        {
            return string.Equals(values["type"].ToString(), "array", StringComparison.InvariantCultureIgnoreCase);
        }

        private static AttributeType GetTypeForAttribute(IDictionary<string, object> values, bool isMultivalued)
        {
            if (isMultivalued)
            {
                return GetTypeForMultivaluedAttribute(values);
            }
            else
            {
                return GetTypeForSingleValuedAttribute(values);
            }
        }

        private static AttributeType GetTypeForSingleValuedAttribute(IDictionary<string, object> values)
        {
            return GetAttributeType(values["type"].ToString().ToLowerInvariant());
        }

        private static AttributeType GetTypeForMultivaluedAttribute(IDictionary<string, object> values)
        {
            if (!(values["items"] is IDictionary<string, object> items))
            {
                throw new ArgumentException("Unknown multivalued data type");
            }

            return GetAttributeType(items["type"].ToString().ToLowerInvariant());
        }

        private static AttributeType GetAttributeType(string typeName)
        {
            switch (typeName)
            {
                case "boolean":
                    return AttributeType.Boolean;

                case "integer":
                    return AttributeType.Integer;

                case "string":
                default:
                    return AttributeType.String;
            }
        }

        private static AttributeOperation GetAttributeOperationFromMutability(string value)
        {
            switch (value.ToUpperInvariant())
            {
                case "READ_WRITE":
                    return AttributeOperation.ImportExport;

                case "WRITE":
                    return AttributeOperation.ExportOnly;

                case "READ":
                default:
                    return AttributeOperation.ImportOnly;
            }
        }

        private static void DumpDictionary(IDictionary<string, object> d1)
        {
            foreach (KeyValuePair<string, object> item in d1)
            {
                logger.Info($"{item.Key}: {item.Value}");

                if (item.Value is IDictionary<string, object> d2)
                {
                    DumpDictionary(d2);
                }
            }
        }
    }
}
