using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.MetadirectoryServices;
using NLog;
using Osdk = Okta.Sdk;
using Newtonsoft.Json;

namespace Lithnet.Okta.ManagementAgent
{
    public static class MASchema
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();

        public static Schema GetMmsSchema(Osdk.OktaClient client)
        {
            Schema mmsSchema = new Schema();
            SchemaType mmsType = MASchema.GetSchemaTypeUser(client);
            mmsSchema.Types.Add(mmsType);

            return mmsSchema;
        }

        private static SchemaType GetSchemaTypeUser(Osdk.OktaClient client)
        {
            SchemaType mmsType = SchemaType.Create("user", true);
            SchemaAttribute mmsAttribute = SchemaAttribute.CreateAnchorAttribute("id", AttributeType.String, AttributeOperation.ImportOnly);
            mmsType.Attributes.Add(mmsAttribute);
            
            mmsAttribute = SchemaAttribute.CreateSingleValuedAttribute("status", AttributeType.String);
            mmsType.Attributes.Add(mmsAttribute);

            mmsAttribute = SchemaAttribute.CreateSingleValuedAttribute("created", AttributeType.String, AttributeOperation.ImportOnly);
            mmsType.Attributes.Add(mmsAttribute);

            mmsAttribute = SchemaAttribute.CreateSingleValuedAttribute("activated", AttributeType.String);
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


            foreach (SchemaAttribute a in MASchema.GetSchemaJson(client))
            {
                mmsType.Attributes.Add(a);
            }

            return mmsType;
        }

        public static IEnumerable<SchemaAttribute> GetSchemaJson(Osdk.OktaClient client)
        {
            Osdk.Resource result = client.GetAsync<Osdk.Resource>(
                new Osdk.Internal.HttpRequest
                {
                    Uri = "/api/v1/meta/schemas/user/default",
                }).Result;


            IDictionary<string, object> definitions = result["definitions"] as IDictionary<string, object>;

            foreach (SchemaAttribute schemaAttribute in MASchema.GetAttributesFromDefinition(definitions, "base"))
            {
                yield return schemaAttribute;
            }

            foreach (SchemaAttribute schemaAttribute in MASchema.GetAttributesFromDefinition(definitions, "custom"))
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
                //logger.Info($"Got property {property.Key}");
                string name = property.Key;
                IDictionary<string, object> values = property.Value as IDictionary<string, object>;

                AttributeOperation operation = MASchema.GetAttributeOperationFromMutability(values["mutability"].ToString());

                AttributeType type = MASchema.GetTypeFromType(values["type"].ToString());

                //logger.Info($"Property definition for {property.Key}/{type}/{operation}");
                if (values["type"].ToString() == "array")
                {
                    yield return SchemaAttribute.CreateMultiValuedAttribute(name, AttributeType.String, operation);
                }
                else
                {
                    yield return SchemaAttribute.CreateSingleValuedAttribute(name, type, operation);
                }
            }
        }

        private static AttributeType GetTypeFromType(string value)
        {
            switch (value.ToLowerInvariant())
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
