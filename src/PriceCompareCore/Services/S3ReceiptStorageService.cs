using System;
using System.IO;
using System.Threading.Tasks;
using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using PriceCompareCore.Config;
using PriceCompareCore.Interfaces;

namespace PriceCompareCore.Services
{
    public class S3ReceiptStorageService : IReceiptStorageService
    {
        private readonly AwsOptions _aws;
        private readonly IAmazonS3 _s3;

        public S3ReceiptStorageService(IOptions<AwsOptions> awsOptions)
        {
            _aws = awsOptions.Value;

            AmazonS3Config config = new AmazonS3Config();
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(_aws.Region);

            _s3 = new AmazonS3Client(
                _aws.AccessKeyId,
                _aws.SecretAccessKey,
                config);
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
