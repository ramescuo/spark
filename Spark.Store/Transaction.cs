﻿/* 
 * Copyright (c) 2014, Furore (info@furore.com) and contributors
 * See the file CONTRIBUTORS for details.
 * 
 * This file is licensed under the BSD 3-Clause license
 * available at https://raw.github.com/furore-fhir/spark/master/LICENSE
 */

using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Builders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Spark.Core;

namespace Spark.Store
{

    public class MongoTransaction
    {
        class Field
        {
            internal const string Key = "id";
            internal const string Transaction = "transaction";
            internal const string Status = "@state";
            internal const string RecordId = "_id";
        }
        class Value
        {
            internal const string Current = "current";
            internal const string Superceded = "superceded";
            internal const string Queued = "queued";
        }

        string transid = null;

        MongoCollection<BsonDocument> collection;

        public MongoTransaction(MongoCollection<BsonDocument> collection)
        {
            this.collection = collection;
        }

        private void MarkExisting(BsonDocument document)
        {
            BsonValue id = document.GetValue(Field.Key);
            IMongoQuery query = Query.And(Query.EQ(Field.Key, id), Query.EQ(Field.Status, Value.Current));
            IMongoUpdate update = new UpdateDocument("$set",
                new BsonDocument
                { 
                    { Field.Transaction, transid },
                }
            );
            collection.Update(query, update, UpdateFlags.Multi);
        }

        public IEnumerable<BsonValue> KeysOf(IEnumerable<BsonDocument> documents)
        {
            foreach(BsonDocument document in documents)
            {
                BsonValue value = null;
                if (document.TryGetValue(Field.Key, out value))
                {
                    yield return value;
                }
            }
        }

        public BsonValue KeyOf(BsonDocument document)
        {
            BsonValue value = null;
            if (document.TryGetValue(Field.Key, out value))
            {
                return value;
            }
            else
            {
                return null;
            }
        }


        public void MarkExisting(IEnumerable<BsonDocument> documents)
        {
            IEnumerable<BsonValue> keys = KeysOf(documents);
            IMongoUpdate update = new UpdateDocument("$set",
                new BsonDocument
                { 
                    { Field.Transaction, this.transid },
                }
            );
            IMongoQuery query = Query.And(Query.EQ(Field.Status, Value.Current),  Query.In(Field.Key, keys));
            collection.Update(query, update, UpdateFlags.Multi);
        }

        public void RemoveQueued(string transid)
        {
            IMongoQuery query = Query.And(
                Query.EQ(Field.Transaction, transid),
                Query.EQ(Field.Status, Value.Queued)
                );
            collection.Remove(query);
        }

        public void RemoveTransaction(string transid)
        {
            IMongoQuery query = Query.EQ(Field.Transaction, transid);
            IMongoUpdate update = new UpdateDocument("$set",
                new BsonDocument
                {
                    { Field.Transaction, 0 }
                }
            );
            collection.Update(query, update, UpdateFlags.Multi);
        }
        
        private void prepareNew(BsonDocument document)
        {
            //document.Remove(Field.RecordId); voor Fhir-documenten niet nodig
            document.Set(Field.Transaction, transid);
            document.Set(Field.Status, Value.Queued);
        }

        private void PrepareNew(IEnumerable<BsonDocument> documents)
        {
            foreach(BsonDocument doc in documents)
            {
                prepareNew(doc);
            }
        }
        
        private void sweep(string transid, string statusfrom, string statusto)
        {
            IMongoQuery query = Query.And(Query.EQ(Field.Transaction, transid), Query.EQ(Field.Status, statusfrom));
            IMongoUpdate update = new UpdateDocument("$set",
                new BsonDocument
                {
                    { Field.Status, statusto }
                }
            );
            collection.Update(query, update, UpdateFlags.Multi);
            
        }

        public void Begin()
        {
            transid = Guid.NewGuid().ToString();
        }

        public void Rollback()
        {
            RemoveQueued(this.transid);
            RemoveTransaction(this.transid);
        }

        public void Commit()
        {
            sweep(transid, Value.Current, Value.Superceded);
            sweep(transid, Value.Queued, Value.Current);
        }
        
        public void Insert(BsonDocument document)
        {
            MarkExisting(document);
            prepareNew(document);
            collection.Save(document);
        }

        public void InsertBatch(List<BsonDocument> documents)
        {
            MarkExisting(documents);
            PrepareNew(documents);
            collection.InsertBatch(documents);
        }

        public void Update(BsonDocument doc)
        {
            MarkExisting(doc);
            prepareNew(doc);
            collection.Save(doc);
        }

        public void Delete(BsonDocument doc)
        {
            MarkExisting(doc);
            prepareNew(doc);
            collection.Save(doc);
        }

        public BsonDocument ReadCurrent(string id)
        {
            IMongoQuery query = 
                Query.And(
                    Query.EQ(Field.Key, id),
                    Query.EQ(Field.Status, Value.Current)
                );
            return collection.FindOne(query);
        }
        
    }
}
