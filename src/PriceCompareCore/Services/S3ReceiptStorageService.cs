using System;
using System.IO;
using System.Threading.Tasks;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PriceCompareCore.Config;
using PriceCompareCore.Interfaces;

namespace PriceCompareCore.Services
{
    public class S3ReceiptStorageService : IReceiptStorageService
    {
        private readonly AwsOptions _aws;
        private readonly IAmazonS3 _s3;
        private readonly ILogger<S3ReceiptStorageService> _logger;

        public S3ReceiptStorageService(
            IOptions<AwsOptions> awsOptions,
            ILogger<S3ReceiptStorageService> logger)
        {
            _aws = awsOptions.Value;
            _logger = logger;

            AmazonS3Config config = new AmazonS3Config();
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(_aws.Region);

            if (!string.IsNullOrWhiteSpace(_aws.AccessKeyId) && !string.IsNullOrWhiteSpace(_aws.SecretAccessKey))
            {
                _s3 = new AmazonS3Client(
                    _aws.AccessKeyId,
                    _aws.SecretAccessKey,
                    config);
                LogStaticCredentials(_aws.AccessKeyId);
            }
            else
            {
                // Fall back to default AWS credentials (IAM role, env, etc.).
                _s3 = new AmazonS3Client(config);
                LogResolvedCredentials();
            }
        }

        private void LogStaticCredentials(string accessKeyId)
        {
            var last4 = accessKeyId?.Length >= 4 ? accessKeyId[^4..] : accessKeyId;
            _logger.LogInformation(
                "AWS S3 using static credentials. AccessKeyId last4={Last4}, Region={Region}.",
                last4,
                _aws.Region);
        }

        private void LogResolvedCredentials()
        {
            try
            {
                var creds = FallbackCredentialsFactory.GetCredentials();
                var imm = creds.GetCredentials();
                var last4 = imm.AccessKey?.Length >= 4 ? imm.AccessKey[^4..] : imm.AccessKey;
                var hasSession = !string.IsNullOrWhiteSpace(imm.Token);

                _logger.LogInformation(
                    "AWS S3 using credential provider {Provider}. AccessKeyId last4={Last4}, SessionTokenPresent={HasSession}, Region={Region}.",
                    creds.GetType().Name,
                    last4,
                    hasSession,
                    _aws.Region);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AWS S3 could not resolve credentials for logging.");
            }
        }

        public async Task<string> UploadAsync(IFormFile file, string userId, int receiptId)
        {
            if (file == null) throw new ArgumentNullException(nameof(file));
            if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("UserId is required.", nameof(userId));

            string extension = Path.GetExtension(file.FileName);
            string key = "user-" + userId + "/receipts/" + receiptId + extension;

            using (Stream stream = file.OpenReadStream())
            {
                var request = new PutObjectRequest
                {
                    BucketName = _aws.ReceiptBucket,
                    Key = key,
                    InputStream = stream,
                    ContentType = file.ContentType
                };

                await _s3.PutObjectAsync(request);
            }

            return key;
        }
    }
}
