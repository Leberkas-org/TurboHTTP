using System;
using System.Buffers;
using System.Net.Http;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Internal;
using TurboHttp.IO.Stages;
using TurboHttp.Streams.Stages;

namespace TurboHttp.Streams;

public class Http11Engine : IHttpProtocolEngine
{
    internal const long MaxBatchWeight = 65_536;
    internal const long MinItemWeight = MaxBatchWeight / 8;

    public BidiFlow<HttpRequestMessage, IOutputItem, IInputItem, HttpResponseMessage, NotUsed> CreateFlow()
    {
        return BidiFlow.FromGraph(GraphDsl.Create(b =>
        {
            var encoder = b.Add(new Http11EncoderStage());
            var decoder = b.Add(new Http11DecoderStage());
            var correlation = b.Add(new Http1XCorrelationStage());

            var requestBCast = b.Add(new Broadcast<HttpRequestMessage>(2));
            var signalMerge = b.Add(new MergePreferred<IOutputItem>(1));

            b.From(requestBCast.Out(0)).To(encoder.Inlet);
            b.From(requestBCast.Out(1)).To(correlation.RequestIn);

            b.From(decoder.Outlet).To(correlation.ResponseIn);

            var signalCast = b.Add(Flow.Create<IControlItem>().Select(IOutputItem (x) => x));

            var batchFlow = b.Add(
                Flow.Create<IOutputItem>()
                    .BatchWeighted(
                        MaxBatchWeight,
                        item => item is DataItem d ? Math.Max(d.Length, MinItemWeight) : 0L,
                        item => item,
                        BatchConsolidate));

            b.From(encoder.Outlet).Via(batchFlow).To(signalMerge.In(0));
            b.From(correlation.OutletSignal).Via(signalCast).To(signalMerge.Preferred);

            return new BidiShape<
                HttpRequestMessage,
                IOutputItem,
                IInputItem,
                HttpResponseMessage>(
                requestBCast.In,
                signalMerge.Out,
                decoder.Inlet,
                correlation.Out);
        }));
    }

    internal static IOutputItem BatchConsolidate(IOutputItem accumulated, IOutputItem next)
    {
        if (accumulated is DataItem accData && next is DataItem nextData)
        {
            var totalLength = accData.Length + nextData.Length;
            var owner = MemoryPool<byte>.Shared.Rent(totalLength);
            accData.Memory.Memory[..accData.Length].CopyTo(owner.Memory);
            nextData.Memory.Memory[..nextData.Length].CopyTo(owner.Memory.Slice(accData.Length));
            accData.Memory.Dispose();
            nextData.Memory.Dispose();
            return new DataItem(owner, totalLength) { Key = accData.Key };
        }

        return next;
    }
}