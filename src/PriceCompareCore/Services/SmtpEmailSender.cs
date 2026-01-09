using System.Threading;
using System.Threading.Tasks;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using PriceCompareCore.Config;
using PriceCompareCore.Interfaces;


namespace PriceCompareCore.Services
{
    public class SmtpEmailSender : IEmailSender
    {
        private readonly EmailOptions _options;

        public SmtpEmailSender(IOptions<EmailOptions> options)
        {
            _options = options.Value;
        }

        public async Task SendAsync(string toEmail, string subject, string htmlBody)
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_options.FromName, _options.FromEmail));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = subject;

            var builder = new BodyBuilder
            {
                HtmlBody = htmlBody
            };
            message.Body = builder.ToMessageBody();

            using var client = new SmtpClient();
            SecureSocketOptions socketOption;
            if (_options.Port == 465)
            {
                socketOption = SecureSocketOptions.SslOnConnect;
            }
            else if (_options.Port == 587)
            {
                socketOption = SecureSocketOptions.StartTls;
            }
            else
            {
                socketOption = _options.UseSsl
                    ? SecureSocketOptions.SslOnConnect
                    : SecureSocketOptions.StartTls;
            }

            await client.ConnectAsync(_options.Host, _options.Port, socketOption);
            if (!string.IsNullOrWhiteSpace(_options.Username))
            {
                await client.AuthenticateAsync(_options.Username, _options.Password);
            }

            await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }
    }
}