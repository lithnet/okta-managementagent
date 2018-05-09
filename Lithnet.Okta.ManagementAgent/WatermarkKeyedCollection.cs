using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using System.Runtime.Serialization;
using System.Xml.Serialization;

namespace Lithnet.Okta.ManagementAgent
{
    [XmlType("Watermarks")]
    public class WatermarkKeyedCollection : KeyedCollection<string, Watermark>
    {
        protected override string GetKeyForItem(Watermark item)
        {
            return item.TableID;
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
