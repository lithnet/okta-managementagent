using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace Lithnet.Okta.ManagementAgent
{
    [XmlType("Watermark")]
    public class Watermark
    {
        [XmlElement]
        public string TableID { get; set; }

        [XmlElement]
        public string Value { get; set; }

        [XmlElement]
        public string Type { get; set; }

        public Watermark()
        {

        }

        public Watermark(string tableID, string value, string type)
        {
            this.TableID = tableID;
            this.Value = value;
            this.Type = type;
        }

        public override string ToString()
        {
            if (this.Type == "DateTime")
            {
                try
                {
                    long ticks = long.Parse(this.Value);
                    return $"{this.TableID}:{new DateTime(ticks):s}";
                }
                catch
                {
                    return $"{this.TableID}:unknown";
                }
            }
            else
            {
                return $"{this.TableID}:{this.Value}";
            }
        }
    }
}
