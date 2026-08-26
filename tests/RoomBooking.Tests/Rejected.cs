using System.ClientModel;
using System.ClientModel.Primitives;

namespace RoomBooking.Tests;

/// <summary>A refusal from the provider, as the client surfaces one.</summary>
public static class Rejected
{
    public static ClientResultException With(int status) => new(new StubResponse(status));

    private sealed class StubResponse(int status) : PipelineResponse
    {
        public override int Status => status;
        public override string ReasonPhrase => "stubbed";
        public override Stream? ContentStream { get; set; }
        public override BinaryData Content => BinaryData.FromString("");
        protected override PipelineResponseHeaders HeadersCore { get; } = new StubHeaders();
        public override BinaryData BufferContent(CancellationToken ct = default) => Content;
        public override ValueTask<BinaryData> BufferContentAsync(CancellationToken ct = default) =>
            ValueTask.FromResult(Content);
        public override void Dispose() { }

        private sealed class StubHeaders : PipelineResponseHeaders
        {
            public override IEnumerator<KeyValuePair<string, string>> GetEnumerator() =>
                Enumerable.Empty<KeyValuePair<string, string>>().GetEnumerator();
            public override bool TryGetValue(string name, out string? value) { value = null; return false; }
            public override bool TryGetValues(string name, out IEnumerable<string>? values) { values = null; return false; }
        }
    }
}
