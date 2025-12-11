using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PriceCompareCore.Config
{
    public class AwsOptions
    {
        public string Region { get; set; }
        public string ReceiptBucket { get; set; }
        public string AccessKeyId { get; set; }
        public string SecretAccessKey { get; set; }
    }

    public class RekognitionOptions
    {
        public float MinConfidence { get; set; }
    }
}