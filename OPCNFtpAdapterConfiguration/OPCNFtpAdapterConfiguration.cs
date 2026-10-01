using Newtonsoft.Json;
using System.Collections.Generic;

namespace Siemens.MOM.Interoperability.Adapter
{
    [ConfigurationClass("FTPAdapter", "Configuration for FTP Adapter")]
    public class FTPAdapterConfiguration : ChannelAdapterConfiguration
    {
        /// <inheritdoc/>
        public override string AdapterConfigurationTypeName => nameof(FTPAdapterConfiguration);

        /// <inheritdoc/>
        public override string AdapterType { get; set; } = "FTPAdapter";

        /// <summary>
        /// The FTP Interval
        /// </summary>
        [ConfigurationProperty("The FTP Check interval.")]
        [JsonProperty(Order = 100)]
        public int FtpInterval { get; set; }

        /// <summary>
        /// The name of the Data Handler plugin
        /// </summary>
        [ConfigurationProperty("The FtpInboundUri")]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate, Order = 120)]
        public string FtpInboundUri { get; set; }

        /// <summary>
        /// The name of the Data Handler plugin
        /// </summary>
        [ConfigurationProperty("The FtpOutboundUri")]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate, Order = 130)]
        public string FtpOutboundUri { get; set; }


    }
}