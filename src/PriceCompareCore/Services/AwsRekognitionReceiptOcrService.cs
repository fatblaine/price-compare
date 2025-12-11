using Amazon;
using Amazon.Rekognition;
using Amazon.Rekognition.Model;
using Microsoft.Extensions.Options;
using PriceCompareCore.Config;

namespace PriceCompareCore.Services
{
    public class AwsRekognitionReceiptOcrService : IReceiptOcrService
    {
        private readonly AwsOptions _aws;
        private readonly RekognitionOptions _rekogOptions;
        private readonly IAmazonRekognition _rekognition;

        public AwsRekognitionReceiptOcrService(
            IOptions<AwsOptions> awsOptions,
            IOptions<RekognitionOptions> rekogOptions)
        {
            _aws = awsOptions.Value ?? throw new ArgumentNullException(nameof(awsOptions));
            _rekogOptions = rekogOptions.Value ?? throw new ArgumentNullException(nameof(rekogOptions));

            var config = new AmazonRekognitionConfig
            {
                RegionEndpoint = RegionEndpoint.GetBySystemName(_aws.Region)
            };

            _rekognition = new AmazonRekognitionClient(_aws.AccessKeyId, _aws.SecretAccessKey, config);
        }

        public async Task<ReceiptOcrResult> AnalyzeAsync(string imageKey)
        {
            if (string.IsNullOrWhiteSpace(imageKey))
            {
                throw new ArgumentException("S3 object key must be provided.", nameof(imageKey));
            }

            var result = new ReceiptOcrResult();
            var request = new DetectTextRequest
            {
                Image = new Image
                {
                    S3Object = new S3Object
                    {
                        Bucket = _aws.ReceiptBucket,
                        Name = imageKey
                    }
                }
            };

            var response = await _rekognition.DetectTextAsync(request);
            var lines = response.TextDetections
                .Where(d => d.Type == TextTypes.LINE
                            && d.Confidence >= _rekogOptions.MinConfidence
                            && !string.IsNullOrWhiteSpace(d.DetectedText))
                .Select(d => new OcrTextLine
                {
                    Text = d.DetectedText,
                    Confidence = d.Confidence
                });

            result.Lines.AddRange(lines);

            return result;
        }
    }
}
