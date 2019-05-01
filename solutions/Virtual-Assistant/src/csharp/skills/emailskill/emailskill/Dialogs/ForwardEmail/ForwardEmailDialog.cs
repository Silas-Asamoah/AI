﻿using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EmailSkill.Dialogs.FindContact;
using EmailSkill.Dialogs.Shared;
using EmailSkill.Dialogs.Shared.Resources;
using EmailSkill.Dialogs.Shared.Resources.Cards;
using EmailSkill.Dialogs.Shared.Resources.Strings;
using EmailSkill.ServiceClients;
using EmailSkill.Util;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Solutions.Responses;
using Microsoft.Bot.Builder.Solutions.Skills;
using Microsoft.Bot.Builder.Solutions.Util;

namespace EmailSkill.Dialogs.ForwardEmail
{
    public class ForwardEmailDialog : EmailSkillDialog
    {
        public ForwardEmailDialog(
            SkillConfigurationBase services,
            ResponseManager responseManager,
            IStatePropertyAccessor<EmailSkillState> emailStateAccessor,
            IStatePropertyAccessor<DialogState> dialogStateAccessor,
            IServiceManager serviceManager,
            IBotTelemetryClient telemetryClient)
            : base(nameof(ForwardEmailDialog), services, responseManager, emailStateAccessor, dialogStateAccessor, serviceManager, telemetryClient)
        {
            TelemetryClient = telemetryClient;

            var forwardEmail = new WaterfallStep[]
            {
                IfClearContextStep,
                GetAuthToken,
                AfterGetAuthToken,
                SetDisplayConfig,
                CollectSelectedEmail,
                AfterCollectSelectedEmail,
                CollectRecipient,
                CollectAdditionalText,
                AfterCollectAdditionalText,
                ConfirmBeforeSending,
                ConfirmAllRecipient,
                ForwardEmail,
            };

            var showEmail = new WaterfallStep[]
            {
                PagingStep,
                ShowEmails,
            };

            var collectRecipients = new WaterfallStep[]
            {
                PromptRecipientCollection,
                GetRecipients,
            };

            var updateSelectMessage = new WaterfallStep[]
            {
                UpdateMessage,
                PromptUpdateMessage,
                AfterUpdateMessage,
            };

            // Define the conversation flow using a waterfall model.
            AddDialog(new WaterfallDialog(Actions.Forward, forwardEmail) { TelemetryClient = telemetryClient });
            AddDialog(new WaterfallDialog(Actions.Show, showEmail) { TelemetryClient = telemetryClient });
            AddDialog(new WaterfallDialog(Actions.CollectRecipient, collectRecipients) { TelemetryClient = telemetryClient });
            AddDialog(new WaterfallDialog(Actions.UpdateSelectMessage, updateSelectMessage) { TelemetryClient = telemetryClient });
            AddDialog(new FindContactDialog(services, responseManager, emailStateAccessor, dialogStateAccessor, serviceManager, telemetryClient));
            InitialDialogId = Actions.Forward;
        }

        public async Task<DialogTurnResult> ForwardEmail(WaterfallStepContext sc, CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                var confirmResult = (bool)sc.Result;
                if (confirmResult)
                {
                    var state = await EmailStateAccessor.GetAsync(sc.Context);

                    var token = state.Token;
                    var message = state.Message;
                    var id = message.FirstOrDefault()?.Id;
                    var recipients = state.FindContactInfor.Contacts;

                    var service = ServiceManager.InitMailService(token, state.GetUserTimeZone(), state.MailSourceType);

                    // send user message.
                    var content = state.Content.Equals(EmailCommonStrings.EmptyContent) ? string.Empty : state.Content;
                    await service.ForwardMessageAsync(id, content, recipients);

                    var emailCard = new EmailCardData
                    {
                        Subject = state.Subject.Equals(EmailCommonStrings.EmptySubject) ? null : state.Subject,
                        EmailContent = state.Content.Equals(EmailCommonStrings.EmptyContent) ? null : state.Content,
                    };
                    emailCard = await ProcessRecipientPhotoUrl(sc.Context, emailCard, state.FindContactInfor.Contacts);

                    var stringToken = new StringDictionary
                    {
                        { "Subject", state.Subject },
                    };

                    var recipientCard = state.FindContactInfor.Contacts.Count() > 5 ? "ConfirmCard_RecipientMoreThanFive" : "ConfirmCard_RecipientLessThanFive";
                    var reply = ResponseManager.GetCardResponse(
                        EmailSharedResponses.SentSuccessfully,
                        new Card("EmailWithOutButtonCard", emailCard),
                        stringToken,
                        "items",
                        new List<Card>().Append(new Card(recipientCard, emailCard)));

                    await sc.Context.SendActivityAsync(reply);
                }
                else
                {
                    await sc.Context.SendActivityAsync(ResponseManager.GetResponse(EmailSharedResponses.CancellingMessage));
                }
            }
            catch (Exception ex)
            {
                await HandleDialogExceptions(sc, ex);

                return new DialogTurnResult(DialogTurnStatus.Cancelled, CommonUtil.DialogTurnResultCancelAllDialogs);
            }

            await ClearConversationState(sc);
            return await sc.EndDialogAsync(true);
        }
    }
}