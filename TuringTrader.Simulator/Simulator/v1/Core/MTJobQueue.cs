﻿//==============================================================================
// Project:     TuringTrader, simulator core
// Name:        MTJobQueue
// Description: multi-threaded job queue to support optimizer
// History:     2018ix20, FUB, created
//------------------------------------------------------------------------------
// Copyright:   (c) 2011-2019, Bertram Solutions LLC
//              https://www.bertram.solutions
// License:     This file is part of TuringTrader, an open-source backtesting
//              engine/ market simulator.
//              TuringTrader is free software: you can redistribute it and/or 
//              modify it under the terms of the GNU Affero General Public 
//              License as published by the Free Software Foundation, either 
//              version 3 of the License, or (at your option) any later version.
//              TuringTrader is distributed in the hope that it will be useful,
//              but WITHOUT ANY WARRANTY; without even the implied warranty of
//              MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.See the
//              GNU Affero General Public License for more details.
//              You should have received a copy of the GNU Affero General Public
//              License along with TuringTrader. If not, see 
//              https://www.gnu.org/licenses/agpl-3.0.
//==============================================================================

//#define NO_THREADS
// when NO_THREADS is defined, QueueJob translates to a plain function call

//#define SINGLE_THREAD
// with SINGLE_THREAD defined, only one worker thread will be used

#region libraries
using System;
using System.Collections.Generic;
using System.Threading;
#endregion

namespace TuringTrader.Simulator
{
    /// <summary>
    /// Multi-threaded job queue. A job queue allows a fixed number of jobs
    /// to be executed in parallel, typically one per CPU core. If the number
    /// of jobs exceeds the maximum parallelism, the jobs are queued until
    /// a slot becomes available. This class is at the core of TuringTrader's
    /// optimizer, but also used for parallel data updating.
    /// </summary>
    public class MTJobQueue
    {
        #region internal data
        private readonly object _queueLock = new object();
        private readonly Queue<Thread> _jobQueue = new Queue<Thread>();
        private readonly int _maximumNumberOfThreads;
        private int _jobsRunning = 0;

        /// <summary>
        /// Set child threads to STA. This may be required for threads interacting w/ GUI elements.
        /// </summary>
        public bool STA { get; set; } = false;
        #endregion

        #region private void CheckQueue()
        private void CheckQueue()
        {
            lock (_queueLock)
            {
                while (_jobsRunning < _maximumNumberOfThreads
                && _jobQueue.Count > 0)
                {
                    _jobsRunning++;
                    Thread nextThread = _jobQueue.Dequeue();
                    nextThread.Start();
                }
            }
        }
        #endregion
        #region private void JobRunner(Action job)
        private void JobRunner(Action job)
        {
            try
            {
                job();

                lock (_queueLock)
                {
                    _jobsRunning--;
                }

                CheckQueue();
            }
            catch (Exception exception)
            {
                Output.WriteLine(
                    string.Format("EXCEPTION: {0}{1}", exception.Message, exception.StackTrace)
                    + Environment.NewLine);
            }
        }
        #endregion

        #region public MTJobQueue(int maxNumberOfThreads = 0)
        /// <summary>
        /// Create and initialize new job queue.
        /// </summary>
        /// <param name="maxNumberOfThreads">maximum number of threads.
        /// If zero, the number of logical cpu cores is used.</param>
        public MTJobQueue(int maxNumberOfThreads = 0)
        {
#if SINGLE_THREAD
                _maximumNumberOfThreads = 1;
#else
            if (maxNumberOfThreads == 0)
            {
                // https://stackoverflow.com/questions/1542213/how-to-find-the-number-of-cpu-cores-via-net-c
                _maximumNumberOfThreads = Environment.ProcessorCount; // number of logical processors
            }
            else
            {
                _maximumNumberOfThreads = maxNumberOfThreads;
            }
#endif
        }
        #endregion
        #region public void QueueJob(Action job)
        /// <summary>
        /// Add job to queue.
        /// </summary>
        /// <param name="job">job action</param>
        public void QueueJob(Action job)
        {
#if NO_THREADS
            job();
#else
            lock (_queueLock)
            {
                Thread queuedThread = new Thread(() => JobRunner(job));

                if (STA)
                    queuedThread.SetApartmentState(ApartmentState.STA);

                _jobQueue.Enqueue(queuedThread);
            }

            CheckQueue();
#endif
        }
        #endregion
        #region public void WaitForCompletion()
        /// <summary>
        /// Wait for completion of all currently queued jobs.
        /// </summary>
        public void WaitForCompletion()
        {
#if NO_THREADS
            // nothing to do
#else
            int? jobsToDo = null;

            do
            {
                if (jobsToDo != null)
                    Thread.Sleep(500);

                lock (_queueLock)
                {
                    jobsToDo = _jobQueue.Count + _jobsRunning;
                }
            } while (jobsToDo > 0);
#endif
        }
        #endregion
    }
}

//==============================================================================
// end of file