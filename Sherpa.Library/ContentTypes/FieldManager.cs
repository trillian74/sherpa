﻿using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.SharePoint.Client;
using Microsoft.SharePoint.Client.Taxonomy;
using Sherpa.Library.ContentTypes.Model;
using Sherpa.Library.Taxonomy;

namespace Sherpa.Library.ContentTypes
{
    public class FieldManager
    {
        private ClientContext ClientContext { get; set; }
        private List<GtField> Fields { get; set; }

        public FieldManager(ClientContext clientContext, List<GtField> fields)
        {
            Fields = fields;
            ValidateConfiguration(fields);
            ClientContext = clientContext;
        }

        public void CreateSiteColumns()
        {
            Web web = ClientContext.Web;
            FieldCollection webFieldCollection = web.Fields;
            ClientContext.Load(webFieldCollection);
            ClientContext.ExecuteQuery();

            var termStoreId = new TaxonomyManager(null).GetTermStoreId(ClientContext);
            foreach (GtField field in Fields.Where(field => !webFieldCollection.Any(item => item.InternalName == field.InternalName)))
            {
                if (field.Type.StartsWith("TaxonomyFieldType"))
                {
                    field.InitializeTaxonomyProperties(termStoreId);
                    DeleteHiddenFieldForTaxonomyField(webFieldCollection, field.ID);
                    CreateTaxonomyField(field, webFieldCollection);
                }
                else
                {
                    CreateField(field, webFieldCollection);
                }
            }
        }

        private void CreateField(GtField field, FieldCollection fields)
        {
            var fieldXml = field.GetFieldAsXml();
            Field newField = fields.AddFieldAsXml(fieldXml, true, AddFieldOptions.AddFieldInternalNameHint);
            ClientContext.Load(newField);
            ClientContext.ExecuteQuery();
        }

        private void CreateTaxonomyField(GtField field, FieldCollection fields)
        {
            var fieldSchema = field.GetFieldAsXml();
            var newField = fields.AddFieldAsXml(fieldSchema, false, AddFieldOptions.AddFieldInternalNameHint);
            ClientContext.Load(newField);
            ClientContext.ExecuteQuery();

            var newTaxonomyField = ClientContext.CastTo<TaxonomyField>(newField);
            newTaxonomyField.SspId = field.SspId;
            newTaxonomyField.TermSetId = field.TermSetId;
            newTaxonomyField.TargetTemplate = String.Empty;
            newTaxonomyField.AnchorId = Guid.Empty;
            newTaxonomyField.Update();
            ClientContext.ExecuteQuery();
        }

        /// <summary>
        /// When a taxonomy field is added, a hidden field is automatically created with the syntax [random letter] + [field id on "N" format]
        /// If a taxonomy field is deleted and then readded, an exception will be thrown if this field is not deleted first.
        /// See  http://blogs.msdn.com/b/boodablog/archive/2014/06/07/a-duplicate-field-name-lt-guid-gt-was-found-re-creating-a-taxonomy-field-using-the-client-object-model.aspx
        /// </summary>
        /// <param name="fields"></param>
        /// <param name="fieldId"></param>
        private void DeleteHiddenFieldForTaxonomyField(FieldCollection fields, Guid fieldId)
        {
            string hiddenFieldName = fieldId.ToString("N").Substring(1);
            var field = fields.FirstOrDefault(f => f.InternalName.EndsWith(hiddenFieldName));
            if (field != null)
            {
                field.DeleteObject();
                ClientContext.ExecuteQuery();
            }
        }

        public void ValidateConfiguration(List<GtField> fields)
        {
            var fieldIdsForEnsuringUniqueness = new List<Guid>();
            var fieldNamesForEnsuringUniqueness = new List<string>();
            foreach (var field in fields)
            {
                if (fieldIdsForEnsuringUniqueness.Contains(field.ID))
                    throw new NotSupportedException("One or more fields have the same Id which is not supported. Field Id " + field.ID);
                if (fieldNamesForEnsuringUniqueness.Contains(field.InternalName))
                    throw new NotSupportedException("One or more fields have the same InternalName which is not supported. Field Id " + field.InternalName);

                fieldIdsForEnsuringUniqueness.Add(field.ID);
                fieldNamesForEnsuringUniqueness.Add(field.InternalName);
            }
        }

        public void DeleteAllCustomFields()
        {
            Web web = ClientContext.Web;
            FieldCollection webFieldCollection = web.Fields;
            ClientContext.Load(webFieldCollection);
            ClientContext.ExecuteQuery();

            var fieldGroups = new List<string>();
            foreach (GtField field in Fields.Where(f => !fieldGroups.Contains(f.Group)))
            {
                fieldGroups.Add(field.Group);
            }
            for (int i = webFieldCollection.Count - 1; i >= 0; i--)
            {
                if (fieldGroups.Contains(webFieldCollection[i].Group))
                {
                    webFieldCollection[i].DeleteObject();
                    try
                    {
                        ClientContext.ExecuteQuery();
                    }
                    catch
                    {
                        Console.WriteLine("Could not delete site column '" + webFieldCollection[i].Title + "' (" + webFieldCollection[i].InternalName + "). This is most probably due to it being in use");
                    }
                }
            }
        }
    }
}