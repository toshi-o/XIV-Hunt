﻿#region License
// The MIT License (MIT)
// 
// Copyright (c) 2014 Emma 'Eniko' Maassen
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
#endregion
using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace TextPlayer.MML {
    /// <summary>
    /// A ready made class that supports multiple simultaneously playing MML tracks.
    /// </summary>
    public abstract class MultiTrackMMLPlayer : IMusicPlayer {
        private bool muted;
        protected TimeSpan startTime;
        protected TimeSpan lastTime;
        private MMLMode mmlMode;

        public MultiTrackMMLPlayer() {
            Tracks = new List<MMLPlayerTrack>();
            Settings = new MMLSettings();
        }

        /// <summary>
        /// Plays a note on a given channel.
        /// </summary>
        /// <param name="note">Note to play.</param>
        /// <param name="channel">Zero-based channel to play the note on.</param>
        /// <param name="time">Current playback time.</param>
        protected abstract void PlayNote(Note note, int channel, TimeSpan time);

        internal virtual void PlayNote(Note note, int channel, MMLPlayerTrack track, TimeSpan time) {
            //if (Muted)
            //    return;

            int index = Tracks.IndexOf(track);
            if (index < 0)
                return;
            PlayNote(note, index, time);
        }

        /// <summary>
        /// Sets the tempo on all tracks.
        /// </summary>
        /// <param name="tempo">Tempo in bpm.</param>
        public virtual void SetTempo(int tempo) {
            if (mmlMode == MMLMode.ArcheAge) // ArcheAge tempo changes only apply to the track they occur in
                return;

            foreach (var track in Tracks) {
                track.Tempo = tempo;
            }
        }

        /// <summary>
        /// Plays the song. Uses DateTime.Now as the starting time.
        /// </summary>
        public virtual void Play() {
            Play(new TimeSpan(DateTime.Now.Ticks));
        }

        /// <summary>
        /// Plays the song using a custom starting time.
        /// </summary>
        /// <param name="currentTime">Time the playing started.</param>
        public virtual void Play(TimeSpan currentTime) {
            foreach (var track in Tracks)
                track.Play(currentTime);

            startTime = currentTime;
            //Update(currentTime);
        }

        /// <summary>
        /// Update this music player. Uses DateTime.Now as the current time.
        /// </summary>
        public virtual void Update() {
            Update(new TimeSpan(DateTime.Now.Ticks));
        }

        /// <summary>
        /// Update this music player using a custom current time.
        /// </summary>
        /// <param name="currentTime">Current player time.</param>
        public virtual void Update(TimeSpan currentTime) {
            if (mmlMode == MMLMode.Mabinogi) {
                while (currentTime >= NextTick && Playing) {
                    foreach (var track in Tracks) {
                        track.Update(track.NextTick);
                    }
                }
            }
            else {
                foreach (var track in Tracks) {
                    track.Update(currentTime);
                }
            }

            lastTime = currentTime;

            if (!Playing) {
                Stop();
            }
        }

        protected virtual void CalculateDuration() {
            bool storedMute = Muted;

            Stop();
            Mute();
            Play(TimeSpan.Zero);

            while (Playing) {
                foreach (var track in Tracks) {
                    track.Update(track.NextTick);
                }

                if (NextTick > Settings.MaxDuration) {
                    throw new SongDurationException("Song exceeded maximum duration " + Settings.MaxDuration);
                }
            }

            Duration = NextTick;

            if (!storedMute)
                Unmute();
        }

        /// <summary>
        /// Stops this music player.
        /// </summary>
        public virtual void Stop() {
            foreach (var track in Tracks)
                track.Stop();

            lastTime = TimeSpan.Zero;
            startTime = TimeSpan.Zero;
        }

        /// <summary>
        /// Seeks to position within the song (relative to TimeSpan.Zero). Uses DateTime.Now as the current time.
        /// </summary>
        /// <param name="position">Position relative to TimeSpan.Zero to seek to.</param>
        public virtual void Seek(TimeSpan position) {
            Seek(new TimeSpan(DateTime.Now.Ticks), position);
        }

        /// <summary>
        /// Seeks to position within the song (relative to TimeSpan.Zero) using a custom current time.
        /// </summary>
        /// <param name="currentTime">Current player time.</param>
        /// <param name="position">Position relative to TimeSpan.Zero to seek to.</param>
        public virtual void Seek(TimeSpan currentTime, TimeSpan position) {
            bool storedMute = Muted;

            Stop();
            Mute();
            Play(currentTime - position);
            Update(currentTime);

            if (!storedMute)
                Unmute();
        }

        /// <summary>
        /// Load MML from a file containing code starting with 'MML@' and ending in ';'
        /// with tracks separated by ','
        /// </summary>
        /// <param name="file">Path to read from.</param>
        /// <param name="maxTracks">Maximum number of tracks allowed, 0 for infinite.</param>
        public void FromFile(string file, int maxTracks) {
            using (StreamReader stream = new StreamReader(file)) {
                Load(stream, maxTracks);
            }
        }

        /// <summary>
        /// Load MML from a file containing code starting with 'MML@' and ending in ';'
        /// with tracks separated by ','
        /// </summary>
        /// <param name="file">Path to read from.</param>
        public void FromFile(string file) {
            FromFile(file, 0);
        }

        /// <summary>
        /// Load MML from a string of code starting with 'MML@' and ending in ';'
        /// with tracks separated by ','
        /// </summary>
        /// <param name="code">MML collection string to load from.</param>
        /// <param name="maxTracks">Maximum number of tracks allowed, 0 for infinite.</param>
        public void Load(string code, int maxTracks) {
            //if (code.Length > settings.MaxSize) {
            //    throw new SongSizeException("Song exceeded maximum length of " + settings.MaxSize);
            //}

            string trimmedCode = code.Trim().TrimEnd('\n', '\r').TrimStart('\n', '\r');

            if (trimmedCode.StartsWith("MML@", StringComparison.InvariantCultureIgnoreCase))
                trimmedCode = trimmedCode.Replace("MML@", "");
            if (trimmedCode.EndsWith(";", StringComparison.InvariantCultureIgnoreCase))
                trimmedCode = trimmedCode.Remove(trimmedCode.Length - 1);

            var tokens = code.Split(',');
            if (tokens.Length > maxTracks && maxTracks > 0)
                throw new MalformedMMLException("Maximum number of tracks exceeded. Count: " + tokens.Length + ", max: " + maxTracks);

            Tracks = new List<MMLPlayerTrack>();
            for (int i = 0; i < tokens.Length; ++i){
                var track = new MMLPlayerTrack(this)
                {
                    Settings = Settings
                };
                track.Load(tokens[i]);
                track.Mode = mmlMode;
                Tracks.Add(track);
            }

            CalculateDuration();
        }

        /// <summary>
        /// Load MML from a string of code starting with 'MML@' and ending in ';'
        /// with tracks separated by ','
        /// </summary>
        /// <param name="code">MML collection string to load from.</param>
        public void Load(string code) {
            Load(code, 0);
        }

        /// <summary>
        /// Load MML from a stream containing code starting with 'MML@' and ending in ';'
        /// with tracks separated by ','
        /// </summary>
        /// <param name="stream">StreamReader object to read from.</param>
        /// <param name="maxTracks">Maximum number of tracks allowed, 0 for infinite.</param>
        public void Load(StreamReader stream, int maxTracks) {
            var strBuilder = new StringBuilder();
            char[] buffer = new char[1024];
            while (!stream.EndOfStream) {
                int bytesRead = stream.ReadBlock(buffer, 0, buffer.Length);
                //if (strBuilder.Length + bytesRead > settings.MaxSize) {
                //    throw new SongSizeException("Song exceeded maximum length of " + settings.MaxSize);
                //}
                strBuilder.Append(buffer, 0, bytesRead);
            }
            Load(strBuilder.ToString(), maxTracks);
        }

        /// <summary>
        /// Load MML from a stream containing code starting with 'MML@' and ending in ';'
        /// with tracks separated by ','
        /// </summary>
        /// <param name="stream">StreamReader object to read from.</param>
        public void Load(StreamReader stream) {
            Load(stream, 0);
        }

        /// <summary>
        /// Mutes this player.
        /// </summary>
        public virtual void Mute() {
            muted = true;
        }

        /// <summary>
        /// Unmutes this player.
        /// </summary>
        public virtual void Unmute() {
            muted = false;
        }

        public List<MMLPlayerTrack> Tracks { get; private set; }
        /// <summary>
        /// Boolean value indicating whether the player is still playing music.
        /// </summary>
        public bool Playing {
            get {
                for (int i = 0; i < Tracks.Count; ++i){
                    if (Tracks[i].Playing)
                        return true;
                }
                return false;
            }
        }
        /// <summary>
        /// Boolean value indicating whether the player is muted.
        /// </summary>
        public bool Muted {
            get {
                return muted;
            }
            set {
                if (muted == value)
                    return;
                if (value)
                    Mute();
                else
                    Unmute();
            }
        }
        /// <summary>
        /// Duration of the song.
        /// </summary>
        public TimeSpan Duration { get; private set; }

        public bool Loop { get; set; }

        private TimeSpan NextTick {
            get {
                long max = 0;
                for (int i = 0; i < Tracks.Count; ++i){
                    max = Math.Max(max, Tracks[i].NextTick.Ticks);
                }
                return new TimeSpan(max);
            }
        }
        public MMLSettings Settings { get; set; }
        public virtual TimeSpan Elapsed { get { return lastTime - startTime; } }
        public MMLMode Mode {
            get {
                return mmlMode;
            }
            set {
                mmlMode = value;
                for (int i = 0; i < Tracks.Count; ++i){
                    Tracks[i].Mode = mmlMode;
                }
            }
        }
    }
}
