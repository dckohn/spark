﻿/* 
 * Copyright (c) 2014, Furore (info@furore.com) and contributors
 * See the file CONTRIBUTORS for details.
 * 
 * This file is licensed under the BSD 3-Clause license
 * available at https://raw.github.com/furore-fhir/spark/master/LICENSE
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Support;

using Spark.Core;
using System.Net;

namespace Spark.Service
{
    public class TransactionImporter
    {
        Mapper<Key, Key> mapper;
        IList<Interaction> interactions;
        ILocalhost localhost;
        IGenerator generator;

        public TransactionImporter(ILocalhost localhost, IGenerator generator)
        {
            this.localhost = localhost;
            this.generator = generator;
            mapper = new Mapper<Key, Key>();
            interactions = new List<Interaction>();
        }

        public void Add(Interaction interaction)
        {
            interactions.Add(interaction);
        }

        public void AddRange(IEnumerable<Interaction> interactions)
        {
            foreach (Interaction i in interactions)
            {
                Add(i);
            }
        }

        public IList<Interaction> Internalize()
        {
            InternalizeKeys();
            InternalizeReferences();
            return interactions;
        }

        void InternalizeKeys()
        {
            foreach (Interaction interaction in this.interactions)
            {
                InternalizeKey(interaction);
                
            }
        }

        void InternalizeReferences()
        {
            foreach (Interaction i in interactions)
            {
                InternalizeReferences(i.Resource);
            }
        }

        Key Remap(Key key)
        {
            Key newKey = generator.NextKey(key).WithoutBase();
            return mapper.Remap(key, newKey);
        }

        Key RemapHistoryOnly(Key key)
        {
            Key newKey = generator.NextHistoryKey(key).WithoutBase();
            return mapper.Remap(key, newKey);
        }

        void InternalizeKey(Interaction interaction)
        {
            if (interaction.IsDeleted) return; 

            Key key = interaction.Key.Clone();

            switch (localhost.GetKeyKind(key))
            {
                case KeyKind.Foreign:
                {
                    interaction.Key = Remap(key);
                    return;
                }
                case KeyKind.Temporary:
                {
                    interaction.Key = Remap(key);
                    return;
                }
                case KeyKind.Local:
                {
                    if (interaction.Method == Bundle.HTTPVerb.PUT)
                    {
                        interaction.Key = RemapHistoryOnly(key);
                    }
                    else
                    {
                        interaction.Key = Remap(key);
                    }
                    return;

                }
                case KeyKind.Internal:
                default:
                {
                    throw new SparkException("Client provided an key without a base: " + interaction.Key.ToString());
                }
            }
        }

        void InternalizeReferences(Resource resource)
        {
            Visitor action = (element, name) =>
            {
                if (element == null) return;

                if (element is ResourceReference)
                {
                    ResourceReference reference = (ResourceReference)element;
                    reference.Url = InternalizeReference(reference.Url);
                }
                else if (element is FhirUri)
                {
                    FhirUri uri = (FhirUri)element;
                    uri.Value = InternalizeReference(uri.Value);
                    //((FhirUri)element).Value = LocalizeReference(new Uri(((FhirUri)element).Value, UriKind.RelativeOrAbsolute)).ToString();
                }
                else if (element is Narrative)
                {
                    Narrative n = (Narrative)element;
                    n.Div = FixXhtmlDiv(n.Div);
                }

            };

            Type[] types = { typeof(ResourceReference), typeof(FhirUri), typeof(Narrative) };

            ResourceVisitor.VisitByType(resource, action, types);
        }

        Key InternalizeReference(Key original)
        {
            KeyKind triage = (localhost.GetKeyKind(original));
            if (triage == KeyKind.Foreign | triage == KeyKind.Temporary)
            {
                Key replacement = mapper.TryGet(original);
                if (replacement != null)
                {
                    return replacement;
                }
                else
                {
                    throw new SparkException(HttpStatusCode.Conflict, "This reference does not point to a resource in the server or the current transaction: {0}", original);
                }
            }
            else if (triage == KeyKind.Local)
            {
                return original.WithoutBase();
            }
            else
            {
                return original;
            }
        }

        Uri InternalizeReference(Uri uri)
        {
            if (uri == null) return null;
            
            if (localhost.IsBaseOf(uri))
            {
                Key key = localhost.UriToKey(uri);
                return InternalizeReference(key).ToUri();
            }
            else
            {
                return uri;
            }
        }

        String InternalizeReference(String uristring)
        {
            if (String.IsNullOrWhiteSpace(uristring)) return uristring;

            Uri uri = new Uri(uristring, UriKind.RelativeOrAbsolute);
            return InternalizeReference(uri).ToString();
        }

        string FixXhtmlDiv(string div)
        {
            try
            {
                XDocument xdoc = XDocument.Parse(div);
                xdoc.VisitAttributes("img", "src", (n) => n.Value = InternalizeReference(n.Value));
                xdoc.VisitAttributes("a", "href", (n) => n.Value = InternalizeReference(n.Value));
                return xdoc.ToString();

            }
            catch
            {
                // illegal xml, don't bother, just return the argument
                // todo: should we really allow illegal xml ? /mh
                return div;
            }

        }

    }

   
}
