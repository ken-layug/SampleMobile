using FirebaseAdmin.Messaging;
using SampleMobile.Application.Abstractions.Push;
using FcmNotification = FirebaseAdmin.Messaging.Notification;

namespace SampleMobile.Infrastructure.Push.Firebase;

/// <summary>Translates the provider-neutral push request into an FCM HTTP v1 message.</summary>
internal static class FcmMessageFactory {
    public const string NotificationIdKey = "notificationId";
    public const string TypeKey = "type";
    public const string ResourceIdKey = "resourceId";

    public static Message Create(PushDispatchRequest request, FirebaseOptions options) {
        var notificationId = request.Message.NotificationId.ToString();

        // Data carries only identifiers; the app fetches the authoritative record from the inbox API.
        var data = new Dictionary<string, string> {
            [NotificationIdKey] = notificationId,
            [TypeKey] = request.Message.Type
        };
        if (!string.IsNullOrWhiteSpace(request.Message.ResourceId)) {
            data[ResourceIdKey] = request.Message.ResourceId;
        }

#pragma warning disable CS0618
        // FirebaseAdmin 3.7 marks Message.Token obsolete in favour of Firebase Installation IDs (Message.Fid).
        // The "fcm" provider id is defined as "address = FCM registration token", which is what the MAUI
        // Firebase bindings return today. Targeting FIDs would be a different address type, so it would get
        // its own provider id rather than silently reinterpreting existing registrations.
        return new Message {
            Token = request.Target.Address,
#pragma warning restore CS0618
            Notification = new FcmNotification {
                Title = request.Message.Title,
                Body = request.Message.Body
            },
            Data = data,
            Android = new AndroidConfig {
                Priority = Priority.High,
                // The tag supports tray replacement. Notification messages ignore CollapseKey;
                // it is not a delivery deduplication guarantee (see docs/firebase-messaging-flow.md).
                CollapseKey = notificationId,
                Notification = new AndroidNotification {
                    Tag = notificationId,
                    ChannelId = string.IsNullOrWhiteSpace(options.AndroidChannelId) ? null : options.AndroidChannelId
                }
            }
        };
    }
}
