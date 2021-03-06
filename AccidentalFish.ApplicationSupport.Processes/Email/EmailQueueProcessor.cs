﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Xml.Linq;
using AccidentalFish.ApplicationSupport.Core.BackoffProcesses;
using AccidentalFish.ApplicationSupport.Core.Blobs;
using AccidentalFish.ApplicationSupport.Core.Components;
using AccidentalFish.ApplicationSupport.Core.Email;
using AccidentalFish.ApplicationSupport.Core.Logging;
using AccidentalFish.ApplicationSupport.Core.Policies;
using AccidentalFish.ApplicationSupport.Core.Queues;
using RazorEngine;

namespace AccidentalFish.ApplicationSupport.Processes.Email
{
    [ComponentIdentity(HostableComponentNames.EmailQueueProcessor)]
    internal class EmailQueueProcessor : BackoffQueueProcessor<EmailQueueItem>
    {
        private class EmailContent
        {
            public string Title { get; set; }

            public string HtmlBody { get; set; }

            public string TextBody { get; set; }
        }

        private const string EmailFullyQualifiedName = "com.accidental-fish.email";
        private readonly IEmailProvider _emailProvider;
        private readonly IAsynchronousQueue<EmailQueueItem> _poisonQueue; 
        private readonly IAsynchronousBlockBlobRepository _blobRepository;
        private readonly ILogger _logger;
        private const int MaximumDeliveryAttempts = 5;

        public EmailQueueProcessor(IApplicationResourceFactory applicationResourceFactory,
            IAsynchronousBackoffPolicy backoffPolicy,
            ILoggerFactory loggerFactory,
            IEmailProvider emailProvider)
            : base(backoffPolicy, applicationResourceFactory.GetAsyncQueue<EmailQueueItem>(new ComponentIdentity(EmailFullyQualifiedName)))
        {
            IComponentIdentity emailComponentIdentity = new ComponentIdentity(EmailFullyQualifiedName);
            _emailProvider = emailProvider;

            string poisonQueueName = applicationResourceFactory.Setting(emailComponentIdentity, "email-poison-queue");

            _poisonQueue = applicationResourceFactory.GetAsyncQueue<EmailQueueItem>(poisonQueueName, emailComponentIdentity);
            _blobRepository = applicationResourceFactory.GetAsyncBlockBlobRepository(emailComponentIdentity);
            _logger = loggerFactory.CreateLongLivedLogger(emailComponentIdentity);
        }

        protected override async Task<bool> HandleRecievedItem(IQueueItem<EmailQueueItem> queueItem)
        {
            EmailQueueItem item = queueItem.Item;
            bool success;
            try
            {
                if (!String.IsNullOrWhiteSpace(item.EmailTemplateId))
                {
                    XDocument template = await GetTemplate(item.EmailTemplateId);
                    EmailContent content = ProcessTemplate(template, item.MergeData);
                    await _emailProvider.Send(item.To, item.Cc, item.From, content.Title, content.HtmlBody, content.TextBody);
                }
                else
                {
                    await _emailProvider.Send(item.To, item.Cc, item.From, item.Subject, item.HtmlBody, item.TextBody);
                }
                
                success = true;
            }
            catch (Exception ex)
            {
                _logger.Error("Error sending email", ex).Wait();
                success = false;
            }

            if (!success && queueItem.DequeueCount >= MaximumDeliveryAttempts)
            {
                success = true;
                try
                {
                    await _poisonQueue.EnqueueAsync(item);
                }
                catch (Exception)
                {
                    success = false;
                }
            }
            
            return success;
        }

        private async Task<XDocument> GetTemplate(string emailTemplateId)
        {
            if (!Path.HasExtension(emailTemplateId))
            {
                emailTemplateId = String.Format("{0}.xml", emailTemplateId);
            }
            IBlob blob = _blobRepository.Get(emailTemplateId);
            string template = await blob.DownloadStringAsync();
            XDocument xdoc = XDocument.Parse(template);
            return xdoc;
        }

        private string TitleTemplateCacheName(string emailTemplateId)
        {
            return String.Format("{0}-title", emailTemplateId);
        }

        private string BodyTemplateCacheName(string emailTemplateId)
        {
            return String.Format("{0}-body", emailTemplateId);
        }

        private EmailContent ProcessTemplate(XDocument template, Dictionary<string, string> mergeData)
        {
            var titleTemplate = template.Root.Element("title");
            var htmlBodyTemplate = template.Root.Element("body") ?? template.Root.Element("html");
            var textBodyTemplate = template.Root.Element("text");
            string title;
            string htmlBody;
            string textBody;

            try
            {
                title = titleTemplate == null ? null : Razor.Parse(titleTemplate.Value, mergeData);
            }
            catch (Exception ex)
            {
                _logger.Error("Error processing title template", ex);
                throw;
            }

            try
            {
                htmlBody = htmlBodyTemplate == null ? null : Razor.Parse(htmlBodyTemplate.Value, mergeData);
            }
            catch (Exception ex)
            {
                _logger.Error("Error processing body template", ex);
                throw;
            }

            try
            {
                textBody = textBodyTemplate == null ? null : Razor.Parse(textBodyTemplate.Value, mergeData);
            }
            catch (Exception ex)
            {
                _logger.Error("Error processing text template", ex);
                throw;
            }

            return new EmailContent
            {
                HtmlBody = htmlBody,
                TextBody = textBody,
                Title = title,
            };
        }

        public override IComponentIdentity ComponentIdentity
        {
            get { return new ComponentIdentity(HostableComponentNames.EmailQueueProcessor); }
        }
    }
}
