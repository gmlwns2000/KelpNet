﻿using System.Linq;
using Cloo;
using KelpNet.Common.Functions;

namespace KelpNet.Common.Activations
{
    public abstract class Activation : NeedPreviousOutputFunction
    {
        public abstract void ForwardActivate(ref Real x);
        public abstract void BackwardActivate(ref Real gy, Real y);

        public abstract string ForwardActivateGPU { get; }
        public abstract string BackwardActivateGPU { get; }

        protected Activation(string name, bool isGpu) : base(name, isGpu)
        {
        }

        public override void InitKernel()
        {
        }

        protected const string ForwardKernelString =
@"
__kernel void {0}(__global Real *gpuY)
{{
	int i = get_global_id(0);

    ForwardActivate(gpuY + i);
}}";

        protected const string BackwardKernelString =
@"
__kernel void {0}(
	__global read_only Real *gpuY,
	__global Real *gpugX)
{{
	int i = get_global_id(0);

    BackwardActivate(gpuY[i], gpugX + i);
}}";

        protected override BatchArray NeedPreviousForward(BatchArray x)
        {
            Real[] y = x.Data.ToArray();

            if (!IsGpu)
            {
                for (int i = 0; i < y.Length; i++)
                {
                    this.ForwardActivate(ref y[i]);
                }
            }
            else
            {
                using (ComputeBuffer<Real> gpuY = new ComputeBuffer<Real>(Weaver.Context, ComputeMemoryFlags.ReadWrite | ComputeMemoryFlags.CopyHostPointer, y))
                {
                    ForwardKernel.SetMemoryArgument(0, gpuY);

                    Weaver.CommandQueue.Execute
                        (
                            ForwardKernel,
                            null,
                            new long[] { x.Data.Length },
                            null,
                            null
                        );

                    Weaver.CommandQueue.Finish();
                    Weaver.CommandQueue.ReadFromBuffer(gpuY, ref y, true, null);
                }
            }

            return BatchArray.Convert(y, x.Shape, x.BatchCount);
        }

        protected override BatchArray NeedPreviousBackward(BatchArray gy, BatchArray prevOutput)
        {
            Real[] gx = gy.Data.ToArray();

            if (!IsGpu)
            {
                for (int i = 0; i < gx.Length; i++)
                {
                    this.BackwardActivate(ref gx[i], prevOutput.Data[i]);
                }
            }
            else
            {
                using (ComputeBuffer<Real> gpuY = new ComputeBuffer<Real>(Weaver.Context, ComputeMemoryFlags.ReadOnly | ComputeMemoryFlags.CopyHostPointer, prevOutput.Data))
                using (ComputeBuffer<Real> gpugX = new ComputeBuffer<Real>(Weaver.Context, ComputeMemoryFlags.ReadWrite | ComputeMemoryFlags.CopyHostPointer, gx))
                {
                    BackwardKernel.SetMemoryArgument(0, gpuY);
                    BackwardKernel.SetMemoryArgument(1, gpugX);

                    Weaver.CommandQueue.Execute
                        (
                            BackwardKernel,
                            null,
                            new long[] { gy.Data.Length },
                            null,
                            null
                        );

                    Weaver.CommandQueue.Finish();
                    Weaver.CommandQueue.ReadFromBuffer(gpugX, ref gx, true, null);
                }
            }

            return BatchArray.Convert(gx, gy.Shape, gy.BatchCount);
        }
    }
}