using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Newtonsoft.Json.Linq;
using Okta.Sdk;
using Okta.Sdk.Internal;

namespace Lithnet.Okta.ManagementAgent
{
    internal static class GenericExtensions
    {
        public static string ConvertToUnsecureString(this SecureString securePassword)
        {
            if (securePassword == null)
                throw new ArgumentNullException(nameof(securePassword));

            IntPtr unmanagedString = IntPtr.Zero;
            try
            {
                unmanagedString = Marshal.SecureStringToGlobalAllocUnicode(securePassword);
                return Marshal.PtrToStringUni(unmanagedString);
            }
            finally
            {
                Marshal.ZeroFreeGlobalAllocUnicode(unmanagedString);
            }
        }

        public static string XmlSerializeToString(this object objectInstance)
        {
            XmlSerializer serializer = new XmlSerializer(objectInstance.GetType());
            StringBuilder sb = new StringBuilder();

            using (TextWriter writer = new StringWriter(sb))
            {
                serializer.Serialize(writer, objectInstance);
            }

            return sb.ToString();
        }

        public static T XmlDeserializeFromString<T>(this string objectData)
        {
            return (T)XmlDeserializeFromString(objectData, typeof(T));
        }

        public static object XmlDeserializeFromString(this string objectData, Type type)
        {
            XmlSerializer serializer = new XmlSerializer(type);
            object result;

            using (TextReader reader = new StringReader(objectData))
            {
                result = serializer.Deserialize(reader);
            }

            return result;
        }

        public static string GetExceptionMessage(this Exception ex)
        {
            if (ex is AggregateException aex)
            {
                if (aex.InnerExceptions.Count == 1)
                {
                    return aex.InnerException.GetExceptionMessage();
                }

                return aex.Message;
            }

            return ex.Message;
        }

        public static string GetExceptionContent(this Exception ex)
        {
            if (ex is AggregateException aex)
            {
                if (aex.InnerExceptions.Count == 1)
                {
                    return aex.InnerException.GetExceptionContent();
                }

                return aex.ToString();
            }

            if (ex is OktaApiException oex)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"{oex.ErrorCode}: {oex.ErrorSummary}");
                sb.AppendLine($"{string.Join("\r\n",oex.GetErrorCauses())}");
                sb.AppendLine(oex.ToString());

                return sb.ToString();
            }

            return ex.ToString();
        }

        public static IEnumerable<string> GetErrorCauses(this OktaApiException ex)
        {
            var internalResource = typeof(OktaApiException).GetField("_resource", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(ex) as IResource;

            if (!(internalResource?.GetData()["errorCauses"] is IList<object> causes))
            {
                yield break;
            }

            foreach (JObject o in causes.OfType<JObject>())
            {
                yield return o.Value<string>("errorSummary");
            }
        }
    }
}

/*
 * {"errorCode":"E0000001",
 * "errorSummary":"Api validation failed: login",
 * "errorLink":"E0000001",
 * "errorId":"oaeBPlwzzB_QNGk7KjLslUO2Q",
 * "errorCauses":[{"errorSummary":"login: An object with this field already exists in the current organization"}]}
 */
