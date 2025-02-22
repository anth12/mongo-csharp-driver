/* Copyright 2010-present MongoDB Inc.
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
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver.Builders;
using MongoDB.Driver.Core.Misc;
using MongoDB.Driver.Core.Operations;

namespace MongoDB.Driver
{
    /// <summary>
    /// An object that can be enumerated to fetch the results of a query. The query is not sent
    /// to the server until you begin enumerating the results.
    /// </summary>
    public abstract class MongoCursor : IEnumerable
    {
        // private fields
        private Collation _collation;
        private readonly MongoCollection _collection;
        private readonly MongoDatabase _database;
        private readonly IMongoQuery _query;
        private readonly MongoServer _server;
        private IMongoFields _fields;
        private BsonDocument _options;
        private QueryFlags _flags;
        private TimeSpan? _maxAwaitTime;
        private ReadConcern _readConcern = ReadConcern.Default;
        private ReadPreference _readPreference;
        private IBsonSerializer _serializer;
        private int _skip;
        private int _limit; // number of documents to return (enforced by cursor)
        private int _batchSize; // number of documents to return in each reply
        private bool _isFrozen; // prevent any further modifications once enumeration has begun

        // constructors
        /// <summary>
        /// Creates a new MongoCursor. It is very unlikely that you will call this constructor. Instead, see all the Find methods in MongoCollection.
        /// </summary>
        /// <param name="collection">The collection.</param>
        /// <param name="query">The query.</param>
        /// <param name="readPreference">The read preference.</param>
        /// <param name="serializer">The serializer.</param>
        protected MongoCursor(MongoCollection collection, IMongoQuery query, ReadPreference readPreference, IBsonSerializer serializer)
        {
            _collection = collection;
            _database = collection.Database;
            _server = collection.Database.Server;
            _query = query;
            _serializer = serializer;
            _readPreference = readPreference;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MongoCursor"/> class.
        /// </summary>
        /// <param name="collection">The collection.</param>
        /// <param name="query">The query.</param>
        /// <param name="readConcern">The read concern.</param>
        /// <param name="readPreference">The read preference.</param>
        /// <param name="serializer">The serializer.</param>
        protected MongoCursor(MongoCollection collection, IMongoQuery query, ReadConcern readConcern, ReadPreference readPreference, IBsonSerializer serializer)
            : this(collection, query, readPreference, serializer)
        {
            _readConcern = readConcern;
        }

        // public properties
        /// <summary>
        /// Gets the server that the query will be sent to.
        /// </summary>
        public virtual MongoServer Server
        {
            get { return _server; }
        }

        /// <summary>
        /// Gets the database that constains the collection that is being queried.
        /// </summary>
        public virtual MongoDatabase Database
        {
            get { return _database; }
        }

        /// <summary>
        /// Gets the collation.
        /// </summary>
        public virtual Collation Collation
        {
            get { return _collation; }
        }

        /// <summary>
        /// Gets the collection that is being queried.
        /// </summary>
        public virtual MongoCollection Collection
        {
            get { return _collection; }
        }

        /// <summary>
        /// Gets the query that will be sent to the server.
        /// </summary>
        public virtual IMongoQuery Query
        {
            get { return _query; }
        }

        /// <summary>
        /// Gets or sets the fields that will be returned from the server.
        /// </summary>
        public virtual IMongoFields Fields
        {
            get { return _fields; }
            set
            {
                if (_isFrozen) { ThrowFrozen(); }
                _fields = value;
            }
        }

        /// <summary>
        /// Gets or sets the cursor options. See also the individual Set{Option} methods, which are easier to use.
        /// </summary>
        public virtual BsonDocument Options
        {
            get { return _options; }
            set
            {
                if (_isFrozen) { ThrowFrozen(); }
                _options = value;
            }
        }

        /// <summary>
        /// Gets or sets the query flags.
        /// </summary>
        public virtual QueryFlags Flags
        {
            get
            {
                if (_readPreference.ReadPreferenceMode == ReadPreferenceMode.Primary)
                {
                    return _flags;
                }
                else
                {
                    return _flags | QueryFlags.SecondaryOk;
                }
            }
            set
            {
                if (_isFrozen) { ThrowFrozen(); }
                _flags = value;
            }
        }

        /// <summary>
        /// Gets or sets the maximum await time for TailableAwait cursors.
        /// </summary>
        /// <value>
        /// The maximum await time for TailableAwait cursors.
        /// </value>
        public virtual TimeSpan? MaxAwaitTime
        {
            get { return _maxAwaitTime; }
            set
            {
                if (_isFrozen) { ThrowFrozen(); }
                _maxAwaitTime = value;
            }
        }

        /// <summary>
        /// Gets the read concern.
        /// </summary>
        public virtual ReadConcern ReadConcern
        {
            get { return _readConcern; }
        }

        /// <summary>
        /// Gets or sets the read preference.
        /// </summary>
        public virtual ReadPreference ReadPreference
        {
            get { return _readPreference; }
            set
            {
                if (_isFrozen) { ThrowFrozen(); }
                _readPreference = value;
            }
        }

        /// <summary>
        /// Gets or sets the number of documents the server should skip before returning the rest of the documents.
        /// </summary>
        public virtual int Skip
        {
            get { return _skip; }
            set
            {
                if (_isFrozen) { ThrowFrozen(); }
                _skip = value;
            }
        }

        /// <summary>
        /// Gets or sets the limit on the number of documents to be returned.
        /// </summary>
        public virtual int Limit
        {
            get { return _limit; }
            set
            {
                if (_isFrozen) { ThrowFrozen(); }
                _limit = value;
            }
        }

        /// <summary>
        /// Gets or sets the batch size (the number of documents returned per batch).
        /// </summary>
        public virtual int BatchSize
        {
            get { return _batchSize; }
            set
            {
                if (_isFrozen) { ThrowFrozen(); }
                _batchSize = value;
            }
        }

        /// <summary>
        /// Gets the serializer.
        /// </summary>
        public virtual IBsonSerializer Serializer
        {
            get { return _serializer; }
        }

        /// <summary>
        /// Gets whether the cursor has been frozen to prevent further changes.
        /// </summary>
        public virtual bool IsFrozen
        {
            get { return _isFrozen; }
            protected set { _isFrozen = value; }
        }

        // public static methods
        /// <summary>
        /// Creates a cursor.
        /// </summary>
        /// <param name="documentType">The type of the returned documents.</param>
        /// <param name="collection">The collection to query.</param>
        /// <param name="query">A query.</param>
        /// <param name="readPreference">The read preference.</param>
        /// <param name="serializer">The serializer.</param>
        /// <returns>
        /// A cursor.
        /// </returns>
        [Obsolete("Use a method that returns a cursor instead.")]
        public static MongoCursor Create(Type documentType, MongoCollection collection, IMongoQuery query, ReadPreference readPreference, IBsonSerializer serializer)
        {
            var cursorDefinition = typeof(MongoCursor<>);
            var cursorType = cursorDefinition.MakeGenericType(documentType);
            var constructorInfo = cursorType.GetTypeInfo().GetConstructor(new Type[] { typeof(MongoCollection), typeof(IMongoQuery), typeof(ReadPreference), typeof(IBsonSerializer) });
            return (MongoCursor)constructorInfo.Invoke(new object[] { collection, query, readPreference, serializer });
        }

        /// <summary>
        /// Creates a cursor.
        /// </summary>
        /// <param name="documentType">Type of the document.</param>
        /// <param name="collection">The collection.</param>
        /// <param name="query">The query.</param>
        /// <param name="readConcern">The read concern.</param>
        /// <param name="readPreference">The read preference.</param>
        /// <param name="serializer">The serializer.</param>
        /// <returns>
        /// A cursor.
        /// </returns>
        [Obsolete("Use a method that returns a cursor instead.")]
        public static MongoCursor Create(Type documentType, MongoCollection collection, IMongoQuery query, ReadConcern readConcern, ReadPreference readPreference, IBsonSerializer serializer)
        {
            var cursorDefinition = typeof(MongoCursor<>);
            var cursorType = cursorDefinition.MakeGenericType(documentType);
            var constructorInfo = cursorType.GetTypeInfo().GetConstructor(new Type[] { typeof(MongoCollection), typeof(IMongoQuery), typeof(ReadConcern), typeof(ReadPreference), typeof(IBsonSerializer) });
            return (MongoCursor)constructorInfo.Invoke(new object[] { collection, query, readConcern, readPreference, serializer });
        }

        // public methods
        /// <summary>
        /// Creates a clone of the cursor.
        /// </summary>
        /// <typeparam name="TDocument">The type of the documents returned.</typeparam>
        /// <returns>A clone of the cursor.</returns>
        public virtual MongoCursor<TDocument> Clone<TDocument>()
        {
            return (MongoCursor<TDocument>)Clone(typeof(TDocument));
        }

        /// <summary>
        /// Creates a clone of the cursor.
        /// </summary>
        /// <typeparam name="TDocument">The type of the documents returned.</typeparam>
        /// <param name="serializer">The serializer to use.</param>
        /// <returns>
        /// A clone of the cursor.
        /// </returns>
        public virtual MongoCursor<TDocument> Clone<TDocument>(IBsonSerializer serializer)
        {
            return (MongoCursor<TDocument>)Clone(typeof(TDocument), serializer);
        }

        /// <summary>
        /// Creates a clone of the cursor.
        /// </summary>
        /// <param name="documentType">The type of the documents returned.</param>
        /// <returns>A clone of the cursor.</returns>
        public virtual MongoCursor Clone(Type documentType)
        {
            var serializer = BsonSerializer.LookupSerializer(documentType);
            return Clone(documentType, serializer);
        }

        /// <summary>
        /// Creates a clone of the cursor.
        /// </summary>
        /// <param name="documentType">The type of the documents returned.</param>
        /// <param name="serializer">The serializer to use.</param>
        /// <returns>
        /// A clone of the cursor.
        /// </returns>
        public virtual MongoCursor Clone(Type documentType, IBsonSerializer serializer)
        {
#pragma warning disable 618
            var clone = Create(documentType, _collection, _query, _readConcern, _readPreference, serializer);
#pragma warning restore
            clone._batchSize = _batchSize;
            clone._collation = _collation;
            clone._fields = _fields;
            clone._flags = _flags;
            clone._limit = _limit;
            clone._maxAwaitTime = _maxAwaitTime;
            clone._options = _options == null ? null : (BsonDocument)_options.Clone();
            clone._skip = _skip;
            return clone;
        }

        /// <summary>
        /// Returns the number of documents that match the query (ignores Skip and Limit, unlike Size which honors them).
        /// </summary>
        /// <returns>The number of documents that match the query.</returns>
        public virtual long Count()
        {
            _isFrozen = true;
            var args = new CountArgs
            {
                Collation = _collation,
                Query = _query,
                ReadPreference = _readPreference
            };
            if (_options != null)
            {
                BsonValue hint;
                if (_options.TryGetValue("$hint", out hint))
                {
                    args.Hint = hint;
                }

                BsonValue maxTimeMS;
                if (_options.TryGetValue("$maxTimeMS", out maxTimeMS))
                {
                    args.MaxTime = TimeSpan.FromMilliseconds(maxTimeMS.ToDouble());
                }
            }
            return _collection.Count(args);
        }

        /// <summary>
        /// Returns an explanation of how the query was executed (instead of the results).
        /// </summary>
        /// <returns>An explanation of thow the query was executed.</returns>
        public virtual BsonDocument Explain()
        {
            return Explain(false);
        }

        /// <summary>
        /// Returns an explanation of how the query was executed (instead of the results).
        /// </summary>
        /// <param name="verbose">Whether the explanation should contain more details.</param>
        /// <returns>An explanation of thow the query was executed.</returns>
        public virtual BsonDocument Explain(bool verbose)
        {
            var verbosity = verbose ? ExplainVerbosity.AllPlansExecution : ExplainVerbosity.QueryPlanner;
            var explainOperation = CreateExplainOperation(verbosity);
            return Collection.UsingImplicitSession(session => ExecuteExplainOperation(session));

            BsonDocument ExecuteExplainOperation(IClientSessionHandle session)
            {
                return Collection.ExecuteReadOperation(session, explainOperation, ReadPreference);
            }
        }

        /// <summary>
        /// Creates an explain operation for this cursor.
        /// </summary>
        /// <returns>An explain operation.</returns>
        protected abstract ExplainOperation CreateExplainOperation(ExplainVerbosity verbosity);

        /// <summary>
        /// Sets the collation.
        /// </summary>
        /// <param name="collation">The collation.</param>
        /// <returns>The cursor (so you can chain method calls to it).</returns>
        public virtual MongoCursor SetCollation(Collation collation)
        {
            if (_isFrozen) { ThrowFrozen(); }
            _collation = collation;
            return this;
        }

        /// <summary>
        /// Sets the batch size (the number of documents returned per batch).
        /// </summary>
        /// <param name="batchSize">The number of documents in each batch.</param>
        /// <returns>The cursor (so you can chain method calls to it).</returns>
        public virtual MongoCursor SetBatchSize(int batchSize)
        {
            if (_isFrozen) { ThrowFrozen(); }
            if (batchSize < 0) { throw new ArgumentException("BatchSize cannot be negative."); }
            _batchSize = batchSize;
            return this;
        }

        /// <summary>
        /// Sets the fields that will be returned from the server.
        /// </summary>
        /// <param name="fields">The fields that will be returned from the server.</param>
        /// <returns>The cursor (so you can chain method calls to it).</returns>
        public virtual MongoCursor SetFields(IMongoFields fields)
        {
            if (_isFrozen) { ThrowFrozen(); }
            _fields = fields;
            return this;
        }

        /// <summary>
        /// Sets the fields that will be returned from the server.
        /// </summary>
        /// <param name="fields">The fields that will be returned from the server.</param>
        /// <returns>The cursor (so you can chain method calls to it).</returns>
        public virtual MongoCursor SetFields(params string[] fields)
        {
            if (_isFrozen) { ThrowFrozen(); }
            _fields = Builders.Fields.Include(fields);
            return this;
        }

        /// <summary>
        /// Sets the query flags.
        /// </summary>
        /// <param name="flags">The query flags.</param>
        /// <returns>The cursor (so you can chain method calls to it).</returns>
        public virtual MongoCursor SetFlags(QueryFlags flags)
        {
            if (_isFrozen) { ThrowFrozen(); }
            _flags = flags;
            return this;
        }

        /// <summary>
        /// Sets the index hint for the query.
        /// </summary>
        /// <param name="hint">The index hint.</param>
        /// <returns>The cursor (so you can chain method calls to it).</returns>
        public virtual MongoCursor SetHint(BsonDocument hint)
        {
            if (_isFrozen) { ThrowFrozen(); }
            SetOption("$hint", hint);
            return this;
        }

        /// <summary>
        /// Sets the index hint for the query.
        /// </summary>
        /// <param name="indexName">The name of the index.</param>
        /// <returns>The cursor (so you can chain method calls to it).</returns>
        public virtual MongoCursor SetHint(string indexName)
        {
            if (_isFrozen) { ThrowFrozen(); }
            SetOption("$hint", indexName);
            return this;
        }

        /// <summary>
        /// Sets the limit on the number of documents to be returned.
        /// </summary>
        /// <param name="limit">The limit on the number of documents to be returned.</param>
        /// <returns>The cursor (so you can chain method calls to it).</returns>
        public virtual MongoCursor SetLimit(int limit)
        {
            if (_isFrozen) { ThrowFrozen(); }
            _limit = limit;
            return this;
        }

        /// <summary>
        /// Sets the max value for the index key range of documents to return (note: the max value itself is excluded from the range).
        /// Often combined with SetHint (if SetHint is not used the server will attempt to determine the matching index automatically).
        /// </summary>
        /// <param name="max">The max value.</param>
        /// <returns>The cursor (so you can chain method calls to it).</returns>
        public virtual MongoCursor SetMax(BsonDocument max)
        {
            if (_isFrozen) { ThrowFrozen(); }
            SetOption("$max", max);
            return this;
        }

        /// <summary>
        /// Sets the maximum await time for tailable await cursors.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <returns>The cursor (so you can chain method calls to it).</returns>
        public virtual MongoCursor SetMaxAwaitTime(TimeSpan? value)
        {
            if (_isFrozen) { ThrowFrozen(); }
            _maxAwaitTime = value;
            return this;
        }

        /// <summary>
        /// Sets the maximum number of documents to scan.
        /// </summary>
        /// <param name="maxScan">The maximum number of documents to scan.</param>
        /// <returns>The cursor (so you can chain method calls to it).</returns>
        [Obsolete("MaxScan was deprecated in server version 4.0.")]
        public virtual MongoCursor SetMaxScan(int maxScan)
        {
            if (_isFrozen) { ThrowFrozen(); }
            SetOption("$maxScan", maxScan);
            return this;
        }

        /// <summary>
        /// Sets the maximum time the server should spend on this query.
        /// </summary>
        /// <param name="maxTime">The max time.</param>
        /// <returns>The cursor (so you can chain method calls to it).</returns>
        public virtual MongoCursor SetMaxTime(TimeSpan maxTime)
        {
            if (_isFrozen) { ThrowFrozen(); }
            SetOption("$maxTimeMS", MaxTimeHelper.ToMaxTimeMS(maxTime));
            return this;
        }

        /// <summary>
        /// Sets the min value for the index key range of documents to return (note: the min value itself is included in the range).
        /// Often combined with SetHint (if SetHint is not used the server will attempt to determine the matching index automatically).
        /// </summary>
        /// <param name="min">The min value.</param>
        /// <returns>The cursor (so you can chain method calls to it).</returns>
        public virtual MongoCursor SetMin(BsonDocument min)
        {
            if (_isFrozen) { ThrowFrozen(); }
            SetOption("$min", min);
            return this;
        }

        /// <summary>
        /// Sets a cursor option.
        /// </summary>
        /// <param name="name">The name of the option.</param>
        /// <param name="value">The value of the option.</param>
        /// <returns>The cursor (so you can chain method calls to it).</returns>
        public virtual MongoCursor SetOption(string name, BsonValue value)
        {
            if (_isFrozen) { ThrowFrozen(); }
            if (_options == null) { _options = new BsonDocument(); }
            _options[name] = value;
            return this;
        }

        /// <summary>
        /// Sets multiple cursor options. See also the individual Set{Option} methods, which are easier to use.
        /// </summary>
        /// <param name="options">The options.</param>
        /// <returns>The cursor (so you can chain method calls to it).</returns>
        public virtual MongoCursor SetOptions(BsonDocument options)
        {
            if (_isFrozen) { ThrowFrozen(); }
            if (options != null)
            {
                if (_options == null) { _options = new BsonDocument(); }
                _options.Merge(options, true); // overwriteExistingElements
            }
            return this;
        }

        /// <summary>
        /// Sets the read preference.
        /// </summary>
        /// <param name="readPreference">The read preference.</param>
        /// <returns>The cursor (so you can chain method calls to it).</returns>
        public virtual MongoCursor SetReadPreference(ReadPreference readPreference)
        {
            if (_isFrozen) { ThrowFrozen(); }
            _readPreference = readPreference;
            return this;
        }

        /// <summary>
        /// Sets the serializer.
        /// </summary>
        /// <param name="serializer">The serializer.</param>
        /// <returns>The cursor (so you can chain method calls to it).</returns>
        public virtual MongoCursor SetSerializer(IBsonSerializer serializer)
        {
            if (_isFrozen) { ThrowFrozen(); }
            _serializer = serializer;
            return this;
        }

        /// <summary>
        /// Sets the $showDiskLoc option.
        /// </summary>
        /// <returns>The cursor (so you can chain method calls to it).</returns>
        public virtual MongoCursor SetShowDiskLoc()
        {
            if (_isFrozen) { ThrowFrozen(); }
            SetOption("$showDiskLoc", true);
            return this;
        }

        /// <summary>
        /// Sets the number of documents the server should skip before returning the rest of the documents.
        /// </summary>
        /// <param name="skip">The number of documents to skip.</param>
        /// <returns>The cursor (so you can chain method calls to it).</returns>
        public virtual MongoCursor SetSkip(int skip)
        {
            if (_isFrozen) { ThrowFrozen(); }
            if (skip < 0) { throw new ArgumentException("Skip cannot be negative."); }
            _skip = skip;
            return this;
        }

        /// <summary>
        /// Sets the $snapshot option.
        /// </summary>
        /// <returns>The cursor (so you can chain method calls to it).</returns>
        [Obsolete("Snapshot was deprecated in server version 3.7.4.")]
        public virtual MongoCursor SetSnapshot()
        {
            if (_isFrozen) { ThrowFrozen(); }
            SetOption("$snapshot", true);
            return this;
        }

        /// <summary>
        /// Sets the sort order for the server to sort the documents by before returning them.
        /// </summary>
        /// <param name="sortBy">The sort order.</param>
        /// <returns>The cursor (so you can chain method calls to it).</returns>
        public virtual MongoCursor SetSortOrder(IMongoSortBy sortBy)
        {
            if (sortBy == null)
            {
                throw new ArgumentNullException("sortBy");
            }
            if (_isFrozen) { ThrowFrozen(); }
            SetOption("$orderby", BsonDocumentWrapper.Create(sortBy));
            return this;
        }

        /// <summary>
        /// Sets the sort order for the server to sort the documents by before returning them.
        /// </summary>
        /// <param name="keys">The names of the fields to sort by.</param>
        /// <returns>The cursor (so you can chain method calls to it).</returns>
        public virtual MongoCursor SetSortOrder(params string[] keys)
        {
            if (_isFrozen) { ThrowFrozen(); }
            return SetSortOrder(SortBy.Ascending(keys));
        }

        /// <summary>
        /// Returns the size of the result set (honors Skip and Limit, unlike Count which does not).
        /// </summary>
        /// <returns>The size of the result set.</returns>
        public virtual long Size()
        {
            _isFrozen = true;
            var args = new CountArgs
            {
                Collation = _collation,
                Query = _query,
                Limit = (_limit == 0) ? (int?)null : _limit,
                ReadPreference = _readPreference,
                Skip = (_skip == 0) ? (int?)null : _skip
            };
            if (_options != null)
            {
                BsonValue hint;
                if (_options.TryGetValue("$hint", out hint))
                {
                    args.Hint = hint;
                }

                BsonValue maxTimeMS;
                if (_options.TryGetValue("$maxTimeMS", out maxTimeMS))
                {
                    args.MaxTime = TimeSpan.FromMilliseconds(maxTimeMS.ToDouble());
                }
            }
            return _collection.Count(args);
        }

        // protected methods
        /// <summary>
        /// Gets the non-generic enumerator.
        /// </summary>
        /// <returns>The enumerator.</returns>
        protected abstract IEnumerator IEnumerableGetEnumerator();

        // private methods
        // funnel exceptions through this method so we can have a single error message
        private void ThrowFrozen()
        {
            throw new InvalidOperationException("A MongoCursor object cannot be modified once it has been frozen.");
        }

        // explicit interface implementations
        IEnumerator IEnumerable.GetEnumerator()
        {
            return IEnumerableGetEnumerator();
        }
    }

    /// <summary>
    /// An object that can be enumerated to fetch the results of a query. The query is not sent
    /// to the server until you begin enumerating the results.
    /// </summary>
    /// <typeparam name="TDocument">The type of the documents returned.</typeparam>
    public class MongoCursor<TDocument> : MongoCursor, IEnumerable<TDocument>
    {
        // constructors
        /// <summary>
        /// Creates a new MongoCursor. It is very unlikely that you will call this constructor. Instead, see all the Find methods in MongoCollection.
        /// </summary>
        /// <param name="collection">The collection.</param>
        /// <param name="query">The query.</param>
        /// <param name="readPreference">The read preference.</param>
        /// <param name="serializer">The serializer.</param>
        [Obsolete("Use a method that returns a cursor instead.")]
        public MongoCursor(MongoCollection collection, IMongoQuery query, ReadPreference readPreference, IBsonSerializer serializer)
            : base(collection, query, readPreference, serializer)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MongoCursor{TDocument}" /> class.
        /// </summary>
        /// <param name="collection">The collection.</param>
        /// <param name="query">The query.</param>
        /// <param name="readConcern">The read concern.</param>
        /// <param name="readPreference">The read preference.</param>
        /// <param name="serializer">The serializer.</param>
        [Obsolete("Use a method that returns a cursor instead.")]
        public MongoCursor(MongoCollection collection, IMongoQuery query, ReadConcern readConcern, ReadPreference readPreference, IBsonSerializer serializer)
                    : base(collection, query, readConcern, readPreference, serializer)
        {
        }

        // public properties
        /// <summary>
        /// Gets the serializer.
        /// </summary>
        public new virtual IBsonSerializer<TDocument> Serializer
        {
            get { return (IBsonSerializer<TDocument>)base.Serializer; }
        }

        // public methods
        /// <summary>
        /// Returns an enumerator that can be used to enumerate the cursor. Normally you will use the foreach statement
        /// to enumerate the cursor (foreach will call GetEnumerator for you).
        /// </summary>
        /// <returns>An enumerator that can be used to iterate over the cursor.</returns>
        public virtual IEnumerator<TDocument> GetEnumerator()
        {
            return Collection.UsingImplicitSession(session => GetEnumerator(session));
        }

        private IEnumerator<TDocument> GetEnumerator(IClientSessionHandle session)
        {
            IsFrozen = true;

            var findOperation = CreateFindOperation();
            var cursor = Collection.ExecuteReadOperation(session, findOperation, ReadPreference);
            return cursor.ToEnumerable().GetEnumerator();
        }

        private FindOperation<TDocument> CreateFindOperation()
        {
            var queryDocument = Query == null ? new BsonDocument() : Query.ToBsonDocument();
            var messageEncoderSettings = Collection.GetMessageEncoderSettings();

            var awaitData = (Flags & QueryFlags.AwaitData) == QueryFlags.AwaitData;
            var exhaust = (Flags & QueryFlags.Exhaust) == QueryFlags.Exhaust;
            var noCursorTimeout = (Flags & QueryFlags.NoCursorTimeout) == QueryFlags.NoCursorTimeout;
            var partialOk = (Flags & QueryFlags.Partial) == QueryFlags.Partial;
            var tailableCursor = (Flags & QueryFlags.TailableCursor) == QueryFlags.TailableCursor;

            if (exhaust)
            {
                throw new NotSupportedException("The Exhaust QueryFlag is not yet supported.");
            }

            var cursorType = Core.Operations.CursorType.NonTailable;
            if (tailableCursor)
            {
                cursorType = Core.Operations.CursorType.Tailable;
                if (awaitData)
                {
                    cursorType = Core.Operations.CursorType.TailableAwait;
                }
            }

            var operation = new FindOperation<TDocument>(new CollectionNamespace(Database.Name, Collection.Name), Serializer, messageEncoderSettings)
            {
                AllowPartialResults = partialOk,
                BatchSize = BatchSize,
                Collation = Collation,
                CursorType = cursorType,
                Filter = queryDocument,
                Limit = Limit,
                MaxAwaitTime = MaxAwaitTime,
#pragma warning disable 618
                Modifiers = Options,
#pragma warning restore 618
                NoCursorTimeout = noCursorTimeout,
                Projection = Fields.ToBsonDocument(),
                ReadConcern = ReadConcern,
                RetryRequested = Server.Settings.RetryReads,
                Skip = Skip
            };

            return operation;
        }

        /// <inheritdoc/>
        protected override ExplainOperation CreateExplainOperation(ExplainVerbosity verbosity)
        {
            var findOperation = CreateFindOperation();
            var explainOperation = new ExplainOperation(
                new DatabaseNamespace(Database.Name),
                explainableOperation: findOperation,
                findOperation.MessageEncoderSettings)
            {
                Verbosity = verbosity
            };
            return explainOperation;
        }

        /// <summary>
        /// Sets the collation.
        /// </summary>
        /// <param name="collation">The collation.</param>
        /// <returns>The cursor (so you can chain method calls to it).</returns>
        public new virtual MongoCursor<TDocument> SetCollation(Collation collation)
        {
            return (MongoCursor<TDocument>)base.SetCollation(collation);
        }

        /// <summary>
        /// Sets the batch size (the number of documents returned per batch).
        /// </summary>
        /// <param name="batchSize">The number of documents in each batch.</param>
        /// <returns>The cursor (so you can chain method calls to it).</returns>
        public new virtual MongoCursor<TDocument> SetBatchSize(int batchSize)
        {
            return (MongoCursor<TDocument>)base.SetBatchSize(batchSize);
        }

        /// <summary>
        /// Sets the fields that will be returned from the server.
        /// </summary>
        /// <param name="fields">The fields that will be returned from the server.</param>
        /// <returns>The cursor (so you can chain method calls to it).</returns>
        public new virtual MongoCursor<TDocument> SetFields(IMongoFields fields)
        {
            return (MongoCursor<TDocument>)base.SetFields(fields);
        }

        /// <summary>
        /// Sets the fields that will be returned from the server.
        /// </summary>
        /// <param name="fields">The fields that will be returned from the server.</param>
        /// <returns>The cursor (so you can chain method calls to it).</returns>
        public new virtual MongoCursor<TDocument> SetFields(params string[] fields)
        {
            return (MongoCursor<TDocument>)base.SetFields(fields);
        }

        /// <summary>
        /// Sets the query flags.
        /// </summary>
        /// <param name="flags">The query flags.</param>
        /// <returns>The cursor (so you can chain method calls to it).</returns>
        public new virtual MongoCursor<TDocument> SetFlags(QueryFlags flags)
        {
            return (MongoCursor<TDocument>)base.SetFlags(flags);
        }

        /// <summary>
        /// Sets the index hint for the query.
        /// </summary>
        /// <param name="hint">The index hint.</param>
        /// <returns>The cursor (so you can chain method calls to it).</returns>
        public new virtual MongoCursor<TDocument> SetHint(BsonDocument hint)
        {
            return (MongoCursor<TDocument>)base.SetHint(hint);
        }

        /// <summary>
        /// Sets the index hint for the query.
        /// </summary>
        /// <param name="indexName">The name of the index.</param>
        /// <returns>The cursor (so you can chain method calls to it).</returns>
        public new virtual MongoCursor<TDocument> SetHint(string indexName)
        {
            return (MongoCursor<TDocument>)base.SetHint(indexName);
        }

        /// <summary>
        /// Sets the limit on the number of documents to be returned.
        /// </summary>
        /// <param name="limit">The limit on the number of documents to be returned.</param>
        /// <returns>The cursor (so you can chain method calls to it).</returns>
        public new virtual MongoCursor<TDocument> SetLimit(int limit)
        {
            return (MongoCursor<TDocument>)base.SetLimit(limit);
        }

        /// <summary>
        /// Sets the max value for the index key range of documents to return (note: the max value itself is excluded from the range).
        /// Often combined with SetHint (if SetHint is not used the server will attempt to determine the matching index automatically).
        /// </summary>
        /// <param name="max">The max value.</param>
        /// <returns>The cursor (so you can chain method calls to it).</returns>
        public new virtual MongoCursor<TDocument> SetMax(BsonDocument max)
        {
            return (MongoCursor<TDocument>)base.SetMax(max);
        }

        /// <summary>
        /// Sets the maximum await time for tailable await cursors.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <returns>The cursor (so you can chain method calls to it).</returns>
        public new virtual MongoCursor SetMaxAwaitTime(TimeSpan? value)
        {
            return (MongoCursor<TDocument>)base.SetMaxAwaitTime(value);
        }

        /// <summary>
        /// Sets the maximum number of documents to scan.
        /// </summary>
        /// <param name="maxScan">The maximum number of documents to scan.</param>
        /// <returns>The cursor (so you can chain method calls to it).</returns>
        [Obsolete("MaxScan was deprecated in server version 4.0.")]
        public new virtual MongoCursor<TDocument> SetMaxScan(int maxScan)
        {
            return (MongoCursor<TDocument>)base.SetMaxScan(maxScan);
        }

        /// <summary>
        /// Sets the maximum time the server should spend on this query.
        /// </summary>
        /// <param name="maxTime">The max time.</param>
        /// <returns>The cursor (so you can chain method calls to it).</returns>
        public new virtual MongoCursor<TDocument> SetMaxTime(TimeSpan maxTime)
        {
            return (MongoCursor<TDocument>)base.SetMaxTime(maxTime);
        }

        /// <summary>
        /// Sets the min value for the index key range of documents to return (note: the min value itself is included in the range).
        /// Often combined with SetHint (if SetHint is not used the server will attempt to determine the matching index automatically).
        /// </summary>
        /// <param name="min">The min value.</param>
        /// <returns>The cursor (so you can chain method calls to it).</returns>
        public new virtual MongoCursor<TDocument> SetMin(BsonDocument min)
        {
            return (MongoCursor<TDocument>)base.SetMin(min);
        }

        /// <summary>
        /// Sets a cursor option.
        /// </summary>
        /// <param name="name">The name of the option.</param>
        /// <param name="value">The value of the option.</param>
        /// <returns>The cursor (so you can chain method calls to it).</returns>
        public new virtual MongoCursor<TDocument> SetOption(string name, BsonValue value)
        {
            return (MongoCursor<TDocument>)base.SetOption(name, value);
        }

        /// <summary>
        /// Sets multiple cursor options. See also the individual Set{Option} methods, which are easier to use.
        /// </summary>
        /// <param name="options">The options.</param>
        /// <returns>The cursor (so you can chain method calls to it).</returns>
        public new virtual MongoCursor<TDocument> SetOptions(BsonDocument options)
        {
            return (MongoCursor<TDocument>)base.SetOptions(options);
        }

        /// <summary>
        /// Sets the read preference.
        /// </summary>
        /// <param name="readPreference">The read preference.</param>
        /// <returns>The cursor (so you can chain method calls to it).</returns>
        public new virtual MongoCursor<TDocument> SetReadPreference(ReadPreference readPreference)
        {
            return (MongoCursor<TDocument>)base.SetReadPreference(readPreference);
        }

        /// <summary>
        /// Sets the serializer.
        /// </summary>
        /// <param name="serializer">The serializer.</param>
        /// <returns>The cursor (so you can chain method calls to it).</returns>
        public virtual MongoCursor<TDocument> SetSerializer(IBsonSerializer<TDocument> serializer)
        {
            return (MongoCursor<TDocument>)base.SetSerializer(serializer);
        }

        /// <summary>
        /// Sets the $showDiskLoc option.
        /// </summary>
        /// <returns>The cursor (so you can chain method calls to it).</returns>
        public new virtual MongoCursor<TDocument> SetShowDiskLoc()
        {
            return (MongoCursor<TDocument>)base.SetShowDiskLoc();
        }

        /// <summary>
        /// Sets the number of documents the server should skip before returning the rest of the documents.
        /// </summary>
        /// <param name="skip">The number of documents to skip.</param>
        /// <returns>The cursor (so you can chain method calls to it).</returns>
        public new virtual MongoCursor<TDocument> SetSkip(int skip)
        {
            return (MongoCursor<TDocument>)base.SetSkip(skip);
        }

        /// <summary>
        /// Sets the $snapshot option.
        /// </summary>
        /// <returns>The cursor (so you can chain method calls to it).</returns>
        [Obsolete("Snapshot was deprecated in server version 3.7.4.")]
        public new virtual MongoCursor<TDocument> SetSnapshot()
        {
            return (MongoCursor<TDocument>)base.SetSnapshot();
        }

        /// <summary>
        /// Sets the sort order for the server to sort the documents by before returning them.
        /// </summary>
        /// <param name="sortBy">The sort order.</param>
        /// <returns>The cursor (so you can chain method calls to it).</returns>
        public new virtual MongoCursor<TDocument> SetSortOrder(IMongoSortBy sortBy)
        {
            return (MongoCursor<TDocument>)base.SetSortOrder(sortBy);
        }

        /// <summary>
        /// Sets the sort order for the server to sort the documents by before returning them.
        /// </summary>
        /// <param name="keys">The names of the fields to sort by.</param>
        /// <returns>The cursor (so you can chain method calls to it).</returns>
        public new virtual MongoCursor<TDocument> SetSortOrder(params string[] keys)
        {
            return (MongoCursor<TDocument>)base.SetSortOrder(keys);
        }

        // protected methods
        /// <summary>
        /// Gets the non-generic enumerator.
        /// </summary>
        /// <returns>The enumerator.</returns>
        protected override IEnumerator IEnumerableGetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
