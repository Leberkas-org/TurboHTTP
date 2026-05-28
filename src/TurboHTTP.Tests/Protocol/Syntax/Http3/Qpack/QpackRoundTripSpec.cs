using TurboHTTP.Protocol.Syntax.Http3.Qpack;

namespace TurboHTTP.Tests.Protocol.Syntax.Http3.Qpack;

public sealed class QpackRoundTripSpec
{
    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.5")]
    public void Should_RoundTrip_StaticOnly()
    {
        var encoder = new QpackEncoder(maxTableCapacity: 0);
        var decoder = new QpackDecoder(maxTableCapacity: 0);

        var headers = new List<(string, string)>
        {
            (":method", "GET"),
            (":path", "/"),
            (":scheme", "https"),
            (":status", "200"),
        };

        var encoded = encoder.Encode(headers);
        var decoded = decoder.Decode(encoded.Span);

        Assert.Equal(headers.Count, decoded.Count);
        for (var i = 0; i < headers.Count; i++)
        {
            Assert.Equal(headers[i].Item1, decoded[i].Name);
            Assert.Equal(headers[i].Item2, decoded[i].Value);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.5")]
    public void Should_RoundTrip_StaticIndexedAndLiteral()
    {
        // Capacity 0 disables dynamic table → forces static refs + pure literals
        var encoder = new QpackEncoder(maxTableCapacity: 0);
        var decoder = new QpackDecoder(maxTableCapacity: 0);

        var headers = new List<(string, string)>
        {
            (":method", "POST"),
            (":path", "/api/v2/data"),
            ("content-type", "application/json"),
            ("x-request-id", "abc-123-def"),
        };

        var encoded = encoder.Encode(headers);
        var decoded = decoder.Decode(encoded.Span);

        Assert.Equal(headers.Count, decoded.Count);
        for (var i = 0; i < headers.Count; i++)
        {
            Assert.Equal(headers[i].Item1, decoded[i].Name);
            Assert.Equal(headers[i].Item2, decoded[i].Value);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.5")]
    public void Should_RoundTrip_DynamicTableEntries()
    {
        var encoder = new QpackEncoder(maxTableCapacity: 4096);
        var decoder = new QpackDecoder(maxTableCapacity: 4096);

        var headers = new List<(string, string)>
        {
            ("x-custom-header", "custom-value-1"),
            ("x-another", "another-value"),
        };

        var encoded = encoder.Encode(headers);

        // Synchronise decoder's dynamic table with encoder's inserts
        SyncDynamicTable(encoder, decoder);

        var decoded = decoder.Decode(encoded.Span);

        Assert.Equal(headers.Count, decoded.Count);
        for (var i = 0; i < headers.Count; i++)
        {
            Assert.Equal(headers[i].Item1, decoded[i].Name);
            Assert.Equal(headers[i].Item2, decoded[i].Value);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.5")]
    public void Should_RoundTrip_RepeatedHeadersReuseDynamicTable()
    {
        var encoder = new QpackEncoder(maxTableCapacity: 4096);
        var decoder = new QpackDecoder(maxTableCapacity: 4096);

        var headers = new List<(string, string)>
        {
            ("x-session", "sess-001"),
            ("x-trace-id", "trace-aaa"),
        };

        // First encode populates the dynamic table
        var encoded1 = encoder.Encode(headers);
        SyncDynamicTable(encoder, decoder);

        var decoded1 = decoder.Decode(encoded1.Span);
        Assert.Equal(headers.Count, decoded1.Count);

        // Second encode should reference existing dynamic table entries
        var encoded2 = encoder.Encode(headers);
        // No new inserts expected — table already has these entries
        var decoded2 = decoder.Decode(encoded2.Span);

        Assert.Equal(headers.Count, decoded2.Count);
        for (var i = 0; i < headers.Count; i++)
        {
            Assert.Equal(headers[i].Item1, decoded2[i].Name);
            Assert.Equal(headers[i].Item2, decoded2[i].Value);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-7.1")]
    public void Should_RoundTrip_SensitiveHeaders()
    {
        var encoder = new QpackEncoder(maxTableCapacity: 4096);
        var decoder = new QpackDecoder(maxTableCapacity: 4096);

        var headers = new List<(string, string)>
        {
            ("authorization", "Bearer eyJhbGciOiJSUzI1NiJ9.test"),
            ("cookie", "session=abc123; theme=dark"),
            ("proxy-authorization", "Basic dXNlcjpwYXNz"),
            ("set-cookie", "id=a3fWa; Max-Age=2592000"),
        };

        var encoded = encoder.Encode(headers);

        // Sensitive headers are NOT inserted into the dynamic table,
        // so no table sync is needed — they use literal encoding.
        var decoded = decoder.Decode(encoded.Span);

        Assert.Equal(headers.Count, decoded.Count);
        for (var i = 0; i < headers.Count; i++)
        {
            Assert.Equal(headers[i].Item1, decoded[i].Name);
            Assert.Equal(headers[i].Item2, decoded[i].Value);
        }

        // Verify encoder did not insert sensitive headers into dynamic table
        Assert.Equal(0, encoder.DynamicTable.Count);
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-7.1")]
    public void Should_RoundTrip_MixedSensitiveAndNormal()
    {
        var encoder = new QpackEncoder(maxTableCapacity: 4096);
        var decoder = new QpackDecoder(maxTableCapacity: 4096);

        var headers = new List<(string, string)>
        {
            (":method", "GET"),
            (":path", "/secure/resource"),
            ("authorization", "Bearer token-xyz"),
            ("accept", "text/html"),
            ("cookie", "lang=en"),
            ("x-request-id", "req-42"),
        };

        var encoded = encoder.Encode(headers);
        SyncDynamicTable(encoder, decoder);

        var decoded = decoder.Decode(encoded.Span);

        Assert.Equal(headers.Count, decoded.Count);
        for (var i = 0; i < headers.Count; i++)
        {
            Assert.Equal(headers[i].Item1, decoded[i].Name);
            Assert.Equal(headers[i].Item2, decoded[i].Value);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.5")]
    public void Should_RoundTrip_LargeHeaderList()
    {
        var encoder = new QpackEncoder(maxTableCapacity: 4096);
        var decoder = new QpackDecoder(maxTableCapacity: 4096);

        var headers = new List<(string, string)>
        {
            (":method", "POST"), // static indexed
            (":path", "/api/v1/submit"), // literal with static name
            (":scheme", "https"), // static indexed
            ("content-type", "application/json"), // static indexed or literal
            ("accept-encoding", "gzip, deflate, br"), // literal with static name
            ("x-forwarded-for", "10.0.0.1"), // dynamic insert
            ("x-correlation-id", "corr-98765"), // dynamic insert
            ("authorization", "Bearer secret-token"), // NEVERINDEX
            ("user-agent", "TurboHttp/1.0"), // literal with static name
            ("x-custom-flag", "enabled"), // dynamic insert
        };

        var encoded = encoder.Encode(headers);
        SyncDynamicTable(encoder, decoder);

        var decoded = decoder.Decode(encoded.Span);

        Assert.Equal(headers.Count, decoded.Count);
        for (var i = 0; i < headers.Count; i++)
        {
            Assert.Equal(headers[i].Item1, decoded[i].Name);
            Assert.Equal(headers[i].Item2, decoded[i].Value);
        }
    }

    [Fact(Timeout = 5000)]
    [Trait("RFC", "RFC9204-4.5")]
    public void Should_RoundTrip_EmptyHeaderList()
    {
        var encoder = new QpackEncoder(maxTableCapacity: 4096);
        var decoder = new QpackDecoder(maxTableCapacity: 4096);

        var encoded = encoder.Encode(new List<(string, string)>());
        var decoded = decoder.Decode(encoded.Span);

        Assert.Empty(decoded);
    }

    private static void SyncDynamicTable(QpackEncoder encoder, QpackDecoder decoder)
    {
        var instructions = encoder.EncoderInstructions;
        if (instructions.Length == 0)
        {
            return;
        }

        var instrDecoder = new QpackInstructionDecoder();
        var parsed = instrDecoder.DecodeAllEncoderInstructions(instructions.Span);

        foreach (var instruction in parsed)
        {
            switch (instruction.Type)
            {
                case EncoderInstructionType.InsertWithNameReference:
                    // Resolve the name from the referenced table
                    var name = instruction.IsStatic
                        ? QpackStaticTable.Entries[instruction.NameIndex].Name
                        : decoder.DynamicTable.GetEntry(
                            decoder.DynamicTable.InsertCount - 1 - instruction.NameIndex)!.Value.Name;
                    decoder.DynamicTable.Insert(name, instruction.Value);
                    break;
                case EncoderInstructionType.InsertWithLiteralName:
                    decoder.DynamicTable.Insert(instruction.Name, instruction.Value);
                    break;
                case EncoderInstructionType.SetDynamicTableCapacity:
                    decoder.DynamicTable.SetCapacity(instruction.IntValue);
                    break;
                case EncoderInstructionType.Duplicate:
                    decoder.DynamicTable.Duplicate(instruction.IntValue);
                    break;
            }
        }
    }
}