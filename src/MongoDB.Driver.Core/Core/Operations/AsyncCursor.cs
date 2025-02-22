/* Copyright 2013-present MongoDB Inc.
*
* Licensed under the Apache License, Version 2.0 (the "License");
* you may not use this file except in compliance with the License.
* You may obtain a copy of the License at
*
* http://www.apache.org/licenses/LICENSE-2.0
*
* Unless required by applicable law or agreed to in writing, software
* distributed under the License is distributed on an "AS IS" BASIS,
* WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
* See the License for the specific language governing permissions and
* limitations under the License.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver.Core.Bindings;
using MongoDB.Driver.Core.Connections;
using MongoDB.Driver.Core.Events;
using MongoDB.Driver.Core.Misc;
using MongoDB.Driver.Core.WireProtocol;
using MongoDB.Driver.Core.WireProtocol.Messages.Encoders;

namespace MongoDB.Driver.Core.Operations
{
    /// <summary>
    /// Represents an async cursor.
    /// </summary>
    /// <typeparam name="TDocument">The type of the documents.</typeparam>
    public class AsyncCursor<TDocument> : IAsyncCursor<TDocument>, ICursorBatchInfo
    {
        #region static
        // private static fields
        private static IBsonSerializer<BsonDocument> __getMoreCommandResultSerializer = new PartiallyRawBsonDocumentSerializer(
            "cursor", new PartiallyRawBsonDocumentSerializer(
                "nextBatch", new RawBsonArraySerializer()));
        #endregion

        // fields
        private readonly int? _batchSize;
        private readonly CollectionNamespace _collectionNamespace;
        private IChannelSource _channelSource;
        private bool _closed;
        private int _count;
        private IReadOnlyList<TDocument> _currentBatch;
        private long _cursorId;
        private bool _disposed;
        private IReadOnlyList<TDocument> _firstBatch;
        private readonly int? _limit;
        private readonly TimeSpan? _maxTime;
        private readonly MessageEncoderSettings _messageEncoderSettings;
        private readonly long? _operationId;
        private BsonDocument _postBatchResumeToken;
        private readonly IBsonSerializer<TDocument> _serializer;
        private readonly bool _wasFirstBatchEmpty;

        // constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="AsyncCursor{TDocument}"/> class.
        /// </summary>
        /// <param name="channelSource">The channel source.</param>
        /// <param name="collectionNamespace">The collection namespace.</param>
        /// <param name="firstBatch">The first batch.</param>
        /// <param name="cursorId">The cursor identifier.</param>
        /// <param name="batchSize">The size of a batch.</param>
        /// <param name="limit">The limit.</param>
        /// <param name="serializer">The serializer.</param>
        /// <param name="messageEncoderSettings">The message encoder settings.</param>
        /// <param name="maxTime">The maxTime for each batch.</param>
        public AsyncCursor(
            IChannelSource channelSource,
            CollectionNamespace collectionNamespace,
            IReadOnlyList<TDocument> firstBatch,
            long cursorId,
            int? batchSize,
            int? limit,
            IBsonSerializer<TDocument> serializer,
            MessageEncoderSettings messageEncoderSettings,
            TimeSpan? maxTime = null)
            : this(
                channelSource,
                collectionNamespace,
                firstBatch,
                cursorId,
                null, // postBatchResumeToken
                batchSize,
                limit,
                serializer,
                messageEncoderSettings,
                maxTime)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="AsyncCursor{TDocument}"/> class.
        /// </summary>
        /// <param name="channelSource">The channel source.</param>
        /// <param name="collectionNamespace">The collection namespace.</param>
        /// <param name="query">The query.</param>
        /// <param name="firstBatch">The first batch.</param>
        /// <param name="cursorId">The cursor identifier.</param>
        /// <param name="batchSize">The size of a batch.</param>
        /// <param name="limit">The limit.</param>
        /// <param name="serializer">The serializer.</param>
        /// <param name="messageEncoderSettings">The message encoder settings.</param>
        /// <param name="maxTime">The maxTime for each batch.</param>
        [Obsolete("Use overload without query.")]
        public AsyncCursor(
            IChannelSource channelSource,
            CollectionNamespace collectionNamespace,
            BsonDocument query,
            IReadOnlyList<TDocument> firstBatch,
            long cursorId,
            int? batchSize,
            int? limit,
            IBsonSerializer<TDocument> serializer,
            MessageEncoderSettings messageEncoderSettings,
            TimeSpan? maxTime = null)
            : this(
                channelSource,
                collectionNamespace,
                query,
                firstBatch,
                cursorId,
                null, // postBatchResumeToken
                batchSize,
                limit,
                serializer,
                messageEncoderSettings,
                maxTime)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="AsyncCursor{TDocument}"/> class.
        /// </summary>
        /// <param name="channelSource">The channel source.</param>
        /// <param name="collectionNamespace">The collection namespace.</param>
        /// <param name="query">The query.</param>
        /// <param name="firstBatch">The first batch.</param>
        /// <param name="cursorId">The cursor identifier.</param>
        /// <param name="postBatchResumeToken">The post batch resume token.</param>
        /// <param name="batchSize">The size of a batch.</param>
        /// <param name="limit">The limit.</param>
        /// <param name="serializer">The serializer.</param>
        /// <param name="messageEncoderSettings">The message encoder settings.</param>
        /// <param name="maxTime">The maxTime for each batch.</param>
        [Obsolete("Use overload without query.")]
        public AsyncCursor(
            IChannelSource channelSource,
            CollectionNamespace collectionNamespace,
            BsonDocument query, // no longer used, so ingore it
            IReadOnlyList<TDocument> firstBatch,
            long cursorId,
            BsonDocument postBatchResumeToken,
            int? batchSize,
            int? limit,
            IBsonSerializer<TDocument> serializer,
            MessageEncoderSettings messageEncoderSettings,
            TimeSpan? maxTime)
            : this(
                  channelSource,
                  collectionNamespace,
                  firstBatch,
                  cursorId,
                  postBatchResumeToken,
                  batchSize,
                  limit,
                  serializer,
                  messageEncoderSettings,
                  maxTime)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="AsyncCursor{TDocument}"/> class.
        /// </summary>
        /// <param name="channelSource">The channel source.</param>
        /// <param name="collectionNamespace">The collection namespace.</param>
        /// <param name="firstBatch">The first batch.</param>
        /// <param name="cursorId">The cursor identifier.</param>
        /// <param name="postBatchResumeToken">The post batch resume token.</param>
        /// <param name="batchSize">The size of a batch.</param>
        /// <param name="limit">The limit.</param>
        /// <param name="serializer">The serializer.</param>
        /// <param name="messageEncoderSettings">The message encoder settings.</param>
        /// <param name="maxTime">The maxTime for each batch.</param>
        public AsyncCursor(
            IChannelSource channelSource,
            CollectionNamespace collectionNamespace,
            IReadOnlyList<TDocument> firstBatch,
            long cursorId,
            BsonDocument postBatchResumeToken,
            int? batchSize,
            int? limit,
            IBsonSerializer<TDocument> serializer,
            MessageEncoderSettings messageEncoderSettings,
            TimeSpan? maxTime)
        {
            _operationId = EventContext.OperationId;
            _channelSource = channelSource;
            _collectionNamespace = Ensure.IsNotNull(collectionNamespace, nameof(collectionNamespace));
            _firstBatch = Ensure.IsNotNull(firstBatch, nameof(firstBatch));
            _cursorId = cursorId;
            _postBatchResumeToken = postBatchResumeToken;
            _batchSize = Ensure.IsNullOrGreaterThanOrEqualToZero(batchSize, nameof(batchSize));
            _limit = Ensure.IsNullOrGreaterThanOrEqualToZero(limit, nameof(limit));
            _serializer = Ensure.IsNotNull(serializer, nameof(serializer));
            _messageEncoderSettings = messageEncoderSettings;
            _maxTime = maxTime;

            if (_limit > 0 && _firstBatch.Count > _limit)
            {
                _firstBatch = _firstBatch.Take(_limit.Value).ToList();
            }
            _count = _firstBatch.Count;
            _wasFirstBatchEmpty = firstBatch.Count == 0;

            DisposeChannelSourceIfNoLongerNeeded();
        }

        // properties
        /// <inheritdoc/>
        public IEnumerable<TDocument> Current
        {
            get
            {
                ThrowIfDisposed();
                return _currentBatch;
            }
        }

        /// <summary>
        /// Gets a value indicating whether the first batch was empty or not.
        /// </summary>
        /// <value>
        ///   <c>true</c> if the first batch was empty; otherwise, <c>false</c>.
        /// </value>
        public bool WasFirstBatchEmpty => _wasFirstBatchEmpty;

        /// <summary>
        /// Gets the post batch resume token.
        /// </summary>
        /// <value>
        /// The post batch resume token.
        /// </value>
        public BsonDocument PostBatchResumeToken
        {
            get { return _postBatchResumeToken; }
        }

        // private methods
        private int CalculateGetMoreNumberToReturn()
        {
            var numberToReturn = _batchSize ?? 0;
            if (_limit > 0)
            {
                var remaining = _limit.Value - _count;
                if (numberToReturn == 0 || numberToReturn > remaining)
                {
                    numberToReturn = remaining;
                }
            }
            return numberToReturn;
        }

        /// <summary>
        /// Closes the cursor.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        public void Close(CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                CloseIfNotAlreadyClosed(cancellationToken);
            }
            finally
            {
                Dispose();
            }
        }

        /// <summary>
        /// Closes the cursor.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task.</returns>
        public async Task CloseAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                await CloseIfNotAlreadyClosedAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                Dispose();
            }
        }

        private CursorBatch<TDocument> CreateCursorBatch(BsonDocument result)
        {
            var cursorDocument = result["cursor"].AsBsonDocument;
            var cursorId = cursorDocument["id"].ToInt64();
            var batch = (RawBsonArray)cursorDocument["nextBatch"];
            var postBatchResumeToken = (BsonDocument)cursorDocument.GetValue("postBatchResumeToken", null);

            using (batch)
            {
                var documents = CursorBatchDeserializationHelper.DeserializeBatch(batch, _serializer, _messageEncoderSettings);
                return new CursorBatch<TDocument>(cursorId, postBatchResumeToken, documents);
            }
        }

        private BsonDocument CreateGetMoreCommand()
        {
            var command = new BsonDocument
            {
                { "getMore", _cursorId },
                { "collection", _collectionNamespace.CollectionName },
                { "batchSize", () => CalculateGetMoreNumberToReturn(), _batchSize > 0 || _limit > 0 },
                { "maxTimeMS", () => MaxTimeHelper.ToMaxTimeMS(_maxTime.Value), _maxTime.HasValue }
            };

            return command;
        }

        private BsonDocument CreateKillCursorsCommand()
        {
            var command = new BsonDocument
            {
                { "killCursors", _collectionNamespace.CollectionName },
                { "cursors", new BsonArray { _cursorId } }
            };

            return command;
        }

        private CursorBatch<TDocument> ExecuteGetMoreCommand(IChannelHandle channel, CancellationToken cancellationToken)
        {
            var command = CreateGetMoreCommand();
            BsonDocument result;
            try
            {
                result = channel.Command<BsonDocument>(
                    _channelSource.Session,
                    null, // readPreference
                    _collectionNamespace.DatabaseNamespace,
                    command,
                    null, // commandPayloads
                    NoOpElementNameValidator.Instance,
                    null, // additionalOptions
                    null, // postWriteAction
                    CommandResponseHandling.Return,
                    __getMoreCommandResultSerializer,
                    _messageEncoderSettings,
                    cancellationToken);
            }
            catch (MongoCommandException ex) when (IsMongoCursorNotFoundException(ex))
            {
                throw new MongoCursorNotFoundException(channel.ConnectionDescription.ConnectionId, _cursorId, command);
            }

            return CreateCursorBatch(result);
        }

        private async Task<CursorBatch<TDocument>> ExecuteGetMoreCommandAsync(IChannelHandle channel, CancellationToken cancellationToken)
        {
            var command = CreateGetMoreCommand();
            BsonDocument result;
            try
            {
                result = await channel.CommandAsync<BsonDocument>(
                    _channelSource.Session,
                    null, // readPreference
                    _collectionNamespace.DatabaseNamespace,
                    command,
                    null, // commandPayloads
                    NoOpElementNameValidator.Instance,
                    null, // additionalOptions
                    null, // postWriteAction
                    CommandResponseHandling.Return,
                    __getMoreCommandResultSerializer,
                    _messageEncoderSettings,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (MongoCommandException ex) when (IsMongoCursorNotFoundException(ex))
            {
                throw new MongoCursorNotFoundException(channel.ConnectionDescription.ConnectionId, _cursorId, command);
            }

            return CreateCursorBatch(result);
        }

        private void ExecuteKillCursorsCommand(IChannelHandle channel, CancellationToken cancellationToken)
        {
            var command = CreateKillCursorsCommand();
            var result = channel.Command(
                _channelSource.Session,
                null, // readPreference
                _collectionNamespace.DatabaseNamespace,
                command,
                null, // commandPayloads
                NoOpElementNameValidator.Instance,
                null, // additionalOptions
                null, // postWriteAction
                CommandResponseHandling.Return,
                BsonDocumentSerializer.Instance,
                _messageEncoderSettings,
                cancellationToken);

            ThrowIfKillCursorsCommandFailed(result, channel.ConnectionDescription.ConnectionId);
        }

        private async Task ExecuteKillCursorsCommandAsync(IChannelHandle channel, CancellationToken cancellationToken)
        {
            var command = CreateKillCursorsCommand();
            var result = await channel.CommandAsync(
                _channelSource.Session,
                null, // readPreference
                _collectionNamespace.DatabaseNamespace,
                command,
                null, // commandPayloads
                NoOpElementNameValidator.Instance,
                null, // additionalOptions
                null, // postWriteAction
                CommandResponseHandling.Return,
                BsonDocumentSerializer.Instance,
                _messageEncoderSettings,
                cancellationToken)
                .ConfigureAwait(false);

            ThrowIfKillCursorsCommandFailed(result, channel.ConnectionDescription.ConnectionId);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases unmanaged and - optionally - managed resources.
        /// </summary>
        /// <param name="disposing"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (!_disposed)
                {
                    CloseIfNotAlreadyClosedFromDispose();

                    if (_channelSource != null)
                    {
                        _channelSource.Dispose();
                    }
                    _disposed = true;
                }
            }
        }

        private void CloseIfNotAlreadyClosed(CancellationToken cancellationToken)
        {
            if (!_closed)
            {
                try
                {
                    if (_cursorId != 0)
                    {
                        try
                        {
                            KillCursors(cancellationToken);
                        }
                        catch
                        {
                            // ignore exceptions
                        }
                    }
                }
                finally
                {
                    _closed = true;
                }
            }
        }

        private async Task CloseIfNotAlreadyClosedAsync(CancellationToken cancellationToken)
        {
            if (!_closed)
            {
                try
                {
                    if (_cursorId != 0)
                    {
                        try
                        {
                            await KillCursorsAsync(cancellationToken).ConfigureAwait(false);
                        }
                        catch
                        {
                            // ignore exceptions
                        }
                    }
                }
                finally
                {
                    _closed = true;
                }
            }
        }

        private void CloseIfNotAlreadyClosedFromDispose()
        {
            try
            {
                using (var source = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                {
                    CloseIfNotAlreadyClosed(source.Token);
                }
            }
            catch
            {
                // ignore any exceptions from CloseIfNotAlreadyClosed when called from Dispose
            }
        }

        private void DisposeChannelSourceIfNoLongerNeeded()
        {
            if (_channelSource != null && _cursorId == 0)
            {
                _channelSource.Dispose();
                _channelSource = null;
            }
        }

        private CursorBatch<TDocument> GetNextBatch(CancellationToken cancellationToken)
        {
            using (EventContext.BeginOperation(_operationId))
            using (var channel = _channelSource.GetChannel(cancellationToken))
            {
                return ExecuteGetMoreCommand(channel, cancellationToken);
            }
        }

        private async Task<CursorBatch<TDocument>> GetNextBatchAsync(CancellationToken cancellationToken)
        {
            using (EventContext.BeginOperation(_operationId))
            using (var channel = await _channelSource.GetChannelAsync(cancellationToken).ConfigureAwait(false))
            {
                return await ExecuteGetMoreCommandAsync(channel, cancellationToken).ConfigureAwait(false);
            }
        }

        private bool IsMongoCursorNotFoundException(MongoCommandException exception)
        {
            return exception.Code == (int)ServerErrorCode.CursorNotFound;
        }

        private void KillCursors(CancellationToken cancellationToken)
        {
            using (EventContext.BeginOperation(_operationId))
            using (EventContext.BeginKillCursors(_collectionNamespace))
            using (var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            using (var channel = _channelSource.GetChannel(cancellationTokenSource.Token))
            {
                if (!channel.Connection.IsExpired)
                {
                    ExecuteKillCursorsCommand(channel, cancellationToken);
                }
            }
        }

        private async Task KillCursorsAsync(CancellationToken cancellationToken)
        {
            using (EventContext.BeginOperation(_operationId))
            using (EventContext.BeginKillCursors(_collectionNamespace))
            using (var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            using (var channel = await _channelSource.GetChannelAsync(cancellationTokenSource.Token).ConfigureAwait(false))
            {
                if (!channel.Connection.IsExpired)
                {
                    await ExecuteKillCursorsCommandAsync(channel, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        /// <inheritdoc/>
        public bool MoveNext(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            bool hasMore;
            if (TryMoveNext(out hasMore))
            {
                return hasMore;
            }

            var batch = GetNextBatch(cancellationToken);
            SaveBatch(batch);
            return true;
        }

        /// <inheritdoc/>
        public async Task<bool> MoveNextAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            bool hasMore;
            if (TryMoveNext(out hasMore))
            {
                return hasMore;
            }

            var batch = await GetNextBatchAsync(cancellationToken).ConfigureAwait(false);
            SaveBatch(batch);
            return true;
        }

        private void SaveBatch(CursorBatch<TDocument> batch)
        {
            var documents = batch.Documents;

            _count += documents.Count;
            if (_limit > 0 && _count > _limit.Value)
            {
                var remove = _count - _limit.Value;
                var take = documents.Count - remove;
                documents = documents.Take(take).ToList();
                _count = _limit.Value;
            }

            _currentBatch = documents;
            _cursorId = batch.CursorId;
            _postBatchResumeToken = batch.PostBatchResumeToken;

            DisposeChannelSourceIfNoLongerNeeded();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(GetType().Name);
            }
        }

        private void ThrowIfKillCursorsCommandFailed(BsonDocument commandResult, ConnectionId connectionId)
        {
            if (!commandResult.GetValue("ok", false).ToBoolean())
            {
                throw new MongoCommandException(connectionId, "Kill cursors command returned an error.", commandResult);
            }
            else
            {
                var notFoundCursors = commandResult["cursorsNotFound"].AsBsonArray;
                if (notFoundCursors.Count > 0)
                {
                    throw new MongoCursorNotFoundException(connectionId, _cursorId, commandResult);
                }

                var killedCursors = commandResult["cursorsKilled"].AsBsonArray.Select(c => c.ToInt64());
                if (!killedCursors.Contains(_cursorId))
                {
                    throw new MongoCommandException(connectionId, "Kill cursors command failed.", commandResult);
                }
            }
        }

        private bool TryMoveNext(out bool hasMore)
        {
            hasMore = false;

            if (_firstBatch != null)
            {
                _currentBatch = _firstBatch;
                _firstBatch = null;
                hasMore = true;
                return true;
            }

            if (_currentBatch == null)
            {
                return true;
            }

            if (_cursorId == 0 || (_limit > 0 && _count == _limit.Value))
            {
                _currentBatch = null;
                return true;
            }

            return false;
        }
    }
}
