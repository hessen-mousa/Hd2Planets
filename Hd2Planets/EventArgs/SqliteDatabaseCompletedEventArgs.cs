using System;

namespace Hd2Planets.EventArgs
{
    internal class SqliteDatabaseCompletedEventArgs : System.EventArgs
    {
        public TimeSpan Duration { get; init; }

        /// <summary>
        /// Returns the duration as a string in the format mm:ss:ffffff
        /// </summary>
        public string DurationString
        {
            get
            {
                return this.Duration.ToString("mm\\:ss\\:ffffff");
            }
        }

        public SqliteDatabaseCompletedEventArgs(TimeSpan duration)
        {
            this.Duration = duration;
        }
    }
}
