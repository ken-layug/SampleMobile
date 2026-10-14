using System.Text.Json.Serialization;
using SampleMobile.Mobile.Core.Push;
using SampleMobile.Mobile.Core.Session;

namespace SampleMobile.Mobile.Core;

/// <summary>Source-generated serializer for state the app persists locally (never sent to the backend).</summary>
[JsonSourceGenerationOptions(System.Text.Json.JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(UserSession))]
[JsonSerializable(typeof(List<PendingCleanup>))]
[JsonSerializable(typeof(RegistrationResult))]
internal sealed partial class MobileStateJsonContext : JsonSerializerContext;
