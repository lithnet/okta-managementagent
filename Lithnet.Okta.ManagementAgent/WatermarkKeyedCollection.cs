using System.Text;
using System.Collections.ObjectModel;
using Newtonsoft.Json;

namespace Lithnet.Okta.ManagementAgent
{
    [JsonArray("watermarks")]
    public class WatermarkKeyedCollection : KeyedCollection<string, Watermark>
    {
        protected override string GetKeyForItem(Watermark item)
        {
            return item.ID;
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();

            foreach (Watermark item in this)
            {
                sb.AppendLine(item.ToString());
            }

            return sb.ToString();
        }
    }
}
