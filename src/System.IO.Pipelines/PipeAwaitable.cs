﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading;
using System.Threading.Tasks;

namespace System.IO.Pipelines
{
    internal struct PipeAwaitable
    {
        private static readonly Action _awaitableIsCompleted = () => { };
        private static readonly Action _awaitableIsNotCompleted = () => { };

        private CancelledState _cancelledState;
        private Action _state;
        private CancellationToken _cancellationToken;
        private CancellationTokenRegistration _cancellationTokenRegistration;

        public PipeAwaitable(bool completed)
        {
            _cancelledState = CancelledState.NotCancelled;
            _state = completed ? _awaitableIsCompleted : _awaitableIsNotCompleted;
        }

        public void AttachToken(CancellationToken cancellationToken, Action<object> callback, object state)
        {
            if (cancellationToken != _cancellationToken)
            {
                _cancellationTokenRegistration.Dispose();
                _cancellationToken = cancellationToken;
                if (_cancellationToken.CanBeCanceled)
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    _cancellationTokenRegistration = _cancellationToken.Register(callback, state);
                }
            }
        }

        public Action Complete()
        {
            var awaitableState = _state;
            _state = _awaitableIsCompleted;

            if (!ReferenceEquals(awaitableState, _awaitableIsCompleted) &&
                !ReferenceEquals(awaitableState, _awaitableIsNotCompleted))
            {
                return awaitableState;
            }
            return null;
        }

        public void Reset()
        {
            if (_state == _awaitableIsCompleted &&
                _cancelledState != CancelledState.CancellationRequested &&
                _cancelledState != CancelledState.CancellationPreRequested)
            {
                _state = _awaitableIsNotCompleted;
            }

            // Change the state from observed -> not cancelled.
            // We only want to reset the cancelled state if it was observed
            if (_cancelledState == CancelledState.CancellationObserved)
            {
                _cancelledState = CancelledState.NotCancelled;
            }
        }

        public bool IsCompleted => ReferenceEquals(_state, _awaitableIsCompleted);

        public Action OnCompleted(Action continuation, ref PipeCompletion completion)
        {
            var awaitableState = _state;
            if (_state == _awaitableIsNotCompleted)
            {
                _state = continuation;
            }

            if (ReferenceEquals(awaitableState, _awaitableIsCompleted))
            {
                return continuation;
            }

            if (!ReferenceEquals(awaitableState, _awaitableIsNotCompleted))
            {
                completion.TryComplete(ThrowHelper.GetInvalidOperationException(ExceptionResource.NoConcurrentOperation));

                _state = _awaitableIsCompleted;

                Task.Run(continuation);
                Task.Run(awaitableState);
            }

            return null;
        }

        public Action Cancel()
        {
            var action = Complete();
            _cancelledState = action == null ?
                CancelledState.CancellationPreRequested :
                CancelledState.CancellationRequested;
            return action;
        }

        public bool ObserveCancelation()
        {
            if (_cancelledState == CancelledState.NotCancelled)
            {
                return false;
            }

            bool isPrerequested = _cancelledState == CancelledState.CancellationPreRequested;

            if (_cancelledState == CancelledState.CancellationRequested || isPrerequested)
            {
                _cancelledState = CancelledState.CancellationObserved;

                // Do not reset awaitable if we were not awaiting in the first place
                if (!isPrerequested)
                {
                    Reset();
                }

                _cancellationToken.ThrowIfCancellationRequested();

                return true;
            }

            return false;
        }

        public override string ToString()
        {
            return $"CancelledState: {_cancelledState}, {nameof(IsCompleted)}: {IsCompleted}";
        }

        private enum CancelledState
        {
            NotCancelled = 0,
            CancellationObserved = 1,
            CancellationPreRequested = 2,
            CancellationRequested = 3,
        }
    }
}
