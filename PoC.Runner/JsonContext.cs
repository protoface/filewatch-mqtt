using PoC.Runner;
using System.Text.Json.Serialization;

namespace PoC.FileWatcherMQTT;

[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Configuration))]
public sealed partial class JsonContext : JsonSerializerContext;