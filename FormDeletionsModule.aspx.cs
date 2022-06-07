using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web;
using System.Web.UI;
using Sitecore.Data.Managers;
using Sitecore.ExperienceForms.Data;
using System.Configuration;
using System.Data.SqlClient;
using Sitecore.Sites;
using System.Text;
using System.Globalization;
using System.Threading.Tasks;
using System.Data;
using System.Web.UI.WebControls;

namespace ContentExportTool
{
    public partial class FormDeletionsModule : Page
    {
        public string _formsConnectionString { get; set; }

        protected void Page_Load(object sender, EventArgs e)
        {
            litFeedback.Text = String.Empty;

            _formsConnectionString = GetConnectionString();

            if (!IsPostBack)
            {
                SetupForm();
            }
        }

        protected void SetupForm()
        {
            var formsFolder = Sitecore.Configuration.Factory.GetDatabase("master").GetItem("/sitecore/Forms");
            if (formsFolder != null)
            {
                var forms = formsFolder.Axes.GetDescendants().Where(x => x.TemplateName == "Form").OrderBy(x => x.Name);
                ddForms.DataSource = ddDeleteWhereSelection.DataSource = forms;
                ddForms.DataTextField = ddDeleteWhereSelection.DataTextField = "Name";
                ddForms.DataValueField = ddDeleteWhereSelection.DataValueField = "ID";
                ddForms.DataBind();
                ddDeleteWhereSelection.DataBind();

                ddDeleteWhereSelection.Items.Insert(0, new ListItem("*Delete From All Forms*", "All"));
                ddDeleteWhereSelection.Items.Insert(0, new ListItem("-Select a form-", ""));

            }
        }


        protected override void OnInit(EventArgs e)
        {
            if (Sitecore.Context.User == null || !Sitecore.Context.User.IsAuthenticated || Sitecore.Context.User.Name.EndsWith("\\anonymous"))
            {
                SiteContext site = Sitecore.Context.Site;
                if (site == null)
                    return;
                this.Response.Redirect(string.Format("{0}?returnUrl={1}", (object)site.LoginPage, (object)HttpUtility.UrlEncode(this.Request.Url.PathAndQuery)));
            }
            base.OnInit(e);
        }

        protected void btnExportForms_Click(object sender, EventArgs e)
        {
            ClearFeedback();
            
            Guid formId;
            if (String.IsNullOrEmpty(ddForms.SelectedValue) || !Guid.TryParse(ddForms.SelectedValue, out formId))
            {
                litExportResponse.Text = "You must enter a valid form ID";
                return;
            }

            var _formDataProvider = Sitecore.DependencyInjection.ServiceLocator.ServiceProvider.GetService(typeof(IFormDataProvider)) as IFormDataProvider;
            var data = _formDataProvider.GetEntries(formId, null, null);

            if (data == null || !data.Any())
            {
                litExportResponse.Text = "No entries found for the given form ID";
                return;
            }

            StartResponse("Form Data - " + ddForms.SelectedItem.Text);
            using (StringWriter sw = new StringWriter())
            {
                var fields = data.FirstOrDefault().Fields.Select(x => x.FieldName);

                var header = "FormItemId,FormEntryId,Created,";
                foreach (var field in fields)
                {

                    header += field + ",";
                }
                header += "Delete";
                sw.WriteLine(header);

                foreach (var entry in data)
                {

                    var itemLine = entry.FormItemId.ToString() + "," + entry.FormEntryId.ToString() + "," + entry.Created.ToString() + ",";

                    // go by field list to maintain the correct order
                    foreach (var field in fields)
                    {
                        var entryField = entry.Fields.FirstOrDefault(x => x.FieldName == field);
                        if (entryField != null)
                        {
                            itemLine += "\"" + entryField.Value + "\",";
                        }
                        else
                        {
                            itemLine += ",";
                        }
                    }

                    sw.WriteLine(itemLine);
                }
                SetCookieAndResponse(sw.ToString());
            }
        }

        protected void btnDeleteFormEntries_Click(object sender, EventArgs e)
        {
            ClearFeedback();
            var output = "";

            var file = uplFormDelete.PostedFile;
            if (file == null || String.IsNullOrEmpty(file.FileName))
            {
                litDeleteByIdResponse.Text = "You must select a file first<br/>";
                return;
            }

            string extension = System.IO.Path.GetExtension(file.FileName);
            if (extension.ToLower() != ".csv")
            {
                litDeleteByIdResponse.Text = "Upload file must be in CSV format<br/>";
                return;
            }          

            if (String.IsNullOrEmpty(_formsConnectionString))
            {
                litDeleteByIdResponse.Text = "Could not find experienceforms connectionstring";
                return;
            }

            var fieldsMap = new List<String>();
            var formEntryId = 0;
            var formItemId = 0;
            var dateIndex = 0;
            var deleteIndex = 0;

            var entriesDelete = 0;
          
            using (TextReader tr = new StreamReader(file.InputStream))
            {
                CsvParser csv = new CsvParser(tr);
                List<string[]> rows = csv.GetRows();
                var language = LanguageManager.DefaultLanguage;
                for (var i = 0; i < rows.Count; i++)
                {
                    var line = i;
                    var cells = rows[i];
                    if (i == 0)
                    {
                        // create fields map
                        fieldsMap = cells.ToList();
                        formEntryId = fieldsMap.FindIndex(x => x.ToLower() == "formentryid");
                        formItemId = fieldsMap.FindIndex(x => x.ToLower() == "formitemid");
                        dateIndex = fieldsMap.FindIndex(x => x.ToLower() == "created");
                        deleteIndex = fieldsMap.FindIndex(x => x.ToLower() == "delete");
                    }
                    else
                    {
                        if (deleteIndex == -1) continue;
                        var delete = cells[deleteIndex];
                        bool shouldDelete;
                        if (!(delete == "1" || bool.TryParse(delete, out shouldDelete)))
                        {
                            continue;
                        }

                        var entryId = cells[formEntryId];

                        litDeleteByIdResponse.Text += DeleteEntryFromTables(entryId, ref entriesDelete);
                    }
                }
            }

            litDeleteByIdResponse.Text += "Deleted " + entriesDelete + " entries";
        }

        protected void btnModifyEntries_Click(object sender, EventArgs e)
        {
            ClearFeedback();

            var output = "";

            var file = uplModifyFile.PostedFile;
            if (file == null || String.IsNullOrEmpty(file.FileName))
            {
                litModifyFeedback.Text = "You must select a file first<br/>";
                return;
            }

            string extension = System.IO.Path.GetExtension(file.FileName);
            if (extension.ToLower() != ".csv")
            {
                litModifyFeedback.Text = "Upload file must be in CSV format<br/>";
                return;
            }

            if (String.IsNullOrEmpty(_formsConnectionString))
            {
                litModifyFeedback.Text = "Could not find experienceforms connectionstring";
                return;
            }

            var fieldsMap = new List<String>();
            var formEntryId = 0;
            var formItemId = 0;

            var entriesUpdated = 0;

            using (TextReader tr = new StreamReader(file.InputStream))
            {
                CsvParser csv = new CsvParser(tr);
                List<string[]> rows = csv.GetRows();
                var language = LanguageManager.DefaultLanguage;
                for (var i = 0; i < rows.Count; i++)
                {
                    var line = i;
                    var cells = rows[i];
                    if (i == 0)
                    {
                        // create fields map
                        fieldsMap = cells.ToList();
                        formEntryId = fieldsMap.FindIndex(x => x.ToLower() == "formentryid");
                        formItemId = fieldsMap.FindIndex(x => x.ToLower() == "formitemid");
                    }
                    else
                    {
                        try
                        {
                            var entryId = cells[formEntryId];
                            var formId = cells[formItemId];

                            var _formDataProvider = Sitecore.DependencyInjection.ServiceLocator.ServiceProvider.GetService(typeof(IFormDataProvider)) as IFormDataProvider;
                            var data = _formDataProvider.GetEntries(new Guid(formId), null, null);

                            var entry = data.FirstOrDefault(x => x.FormEntryId.Equals(new Guid(entryId)));
                            if (entry == null)
                            {
                                throw new Exception("Entry with ID " + entryId + " not found");
                            }
                         
                            //var newEntry = new Sitecore.ExperienceForms.Data.Entities.FormEntry();
                            foreach (var field in fieldsMap)
                            {
                                if (field == "FormItemId" || field == "FormEntryId" || field == "Delete" || field == "Created")
                                    continue;

                                var fieldIndex = fieldsMap.IndexOf(field);
                                if (fieldIndex < 0) continue;
                                var entryField = entry.Fields.FirstOrDefault(x => x.FieldName == field);

                                if (entryField == null)
                                {
                                    // don't try to create a new field with an empty value
                                    if (String.IsNullOrEmpty(cells[fieldIndex]))
                                        continue;

                                    // see if we can find out what the type is
                                    var entryWithField = data.FirstOrDefault(x => x.Fields.Any(y => y.FieldName == field));
                                    if (entryWithField == null)
                                    {
                                        litModifyFeedback.Text += "Could not set " + field + " for entry " + entryId + " because FieldType is unknown<br/>";
                                        continue;
                                    }

                                    var typeField = entryWithField.Fields.FirstOrDefault(x => x.FieldName == field);

                                    entry.Fields.Add(new Sitecore.ExperienceForms.Data.Entities.FieldData()
                                    {
                                        FieldName = field,
                                        ValueType = typeField.ValueType,
                                        FieldItemId = typeField.FieldItemId,
                                        FormEntryId = entry.FormEntryId,
                                        FieldDataId = Guid.NewGuid(),
                                        Value = cells[fieldIndex],
                                    });
                                }
                                else
                                {
                                    entryField.Value = cells[fieldIndex];
                                }
                                
                            }
                            _formDataProvider.CreateEntry(entry);
                            entriesUpdated++;
                        }
                        catch(Exception ex)
                        {
                            litModifyFeedback.Text += "Error modifying row " + (i + 1) + ": " + ex.Message + "<br/>";
                        }
                    }
                }
            }

            litModifyFeedback.Text += "Modified " + entriesUpdated + " entries";
        }

        protected void btnDeleteWhere_Click(object sender, EventArgs e)
        {
            ClearFeedback();

            var formId = ddDeleteWhereSelection.SelectedValue;

            if (String.IsNullOrEmpty(formId)) {
                litFeedbackWhere.Text = "You must select a form to delete from";
                return;
            }

            var field = inptFieldName.Text;
            var value = inptFieldValue.Text;

            if (String.IsNullOrEmpty(field) || String.IsNullOrEmpty(value))
            {
                litFeedbackWhere.Text = "You must specify and field name and value";
                return;
            }

            List<Guid> entryIds = new List<Guid>();
            var _formDataProvider = Sitecore.DependencyInjection.ServiceLocator.ServiceProvider.GetService(typeof(IFormDataProvider)) as IFormDataProvider;

            List<Guid> formIds = new List<Guid>();

            var allFormsInfo = "";

            if (formId == "All")
            {
                var formsFolder = Sitecore.Configuration.Factory.GetDatabase("master").GetItem("/sitecore/Forms");
                var allForms = formsFolder.Axes.GetDescendants().Where(x => x.TemplateName == "Form");

                formIds = allForms.Select(x => x.ID.Guid).ToList();
            }
            else
            {
                formIds.Add(new Guid(formId));                
            }
          
            foreach (var id in formIds)
            {
                var formCount = 0;
                var data = _formDataProvider.GetEntries(id, null, null);

                foreach (var entry in data)
                {
                    var entryField = entry.Fields.FirstOrDefault(x => x.FieldName.ToLower() == field.ToLower());
                    if (entryField == null) continue;

                    var entryValue = entryField.Value;

                    if (!chkCaseSensitive.Checked)
                    {
                        entryValue = entryValue.ToLower();
                        value = value.ToLower();
                    }

                    if (radioEquals.Checked)
                    {
                        if (entryValue.Equals(value))
                        {
                            entryIds.Add(entry.FormEntryId);
                            formCount++;
                        }
                    }
                    else if (radioContaines.Checked)
                    {
                        if (entryValue.Contains(value))
                        {
                            entryIds.Add(entry.FormEntryId);
                            formCount++;
                        }
                    }
                  
                }

                if (formId == "All" && formCount > 0)
                {
                    var formName = Sitecore.Configuration.Factory.GetDatabase("master").GetItem(id.ToString()).Name;
                    allFormsInfo += "Found " + formCount + " matching entries in " + formName + "<br/>";
                }
            }

            if (!entryIds.Any())
            {
                litFeedbackWhere.Text = "No matching entries found";
                return;
            }

            var entriesDelete = 0;
            foreach (var entryId in entryIds)
            {
                litFeedbackWhere.Text += DeleteEntryFromTables(entryId.ToString(), ref entriesDelete);
            }

            litFeedbackWhere.Text += allFormsInfo + "<br/>Deleted " + entriesDelete + " entries";
        }

        private void ClearFeedback()
        {
            litFeedback.Text = "";
            litFeedbackWhere.Text = "";
            litExportResponse.Text = "";
            litDeleteByIdResponse.Text = "";
        }

        private string GetConnectionString()
        {
            var connectionStrings = ConfigurationManager.ConnectionStrings;
            string formsConnectionString = "";
            if (connectionStrings.Count > 0)
            {
                foreach (ConnectionStringSettings connectionString in connectionStrings)
                {
                    var name = connectionString.Name;

                    if (name.ToLower() == "experienceforms")
                    {
                        formsConnectionString = connectionString.ConnectionString;
                    }
                }
            }
            return formsConnectionString;
        }

        protected string DeleteEntryFromTables(string entryId, ref int entriesDelete)
        {
            var output = "";
            var isSitecore10 = IsSitecore10(_formsConnectionString);

            var fieldDataTable = isSitecore10 ? "[sitecore_forms_storage].[FieldData]" : "[FieldData]";

            string commandText = "DELETE from " + fieldDataTable + " WHERE FormEntryId = @EntryId";


            // delete from field data table
            try
            {
                RunSqlCommand(commandText, entryId, _formsConnectionString);
            }
            catch (Exception ex)
            {
                output += "Error deleting " + entryId + " from FieldData table: " + ex.ToString();
                Sitecore.Diagnostics.Log.Error("Forms database communication error.", ex, (object)this);
            }


            // delete from entries table
            var formEntryTable = isSitecore10 ? "[sitecore_forms_storage].[FormEntries]" : "[FormEntry]";
            commandText = "DELETE from " + formEntryTable + " WHERE ID = @EntryId";
            try
            {
                RunSqlCommand(commandText, entryId, _formsConnectionString);
                entriesDelete++;
            }
            catch (Exception ex)
            {
                output += "Error deleting " + entryId + " from Form Entry table: " + ex.ToString();
                Sitecore.Diagnostics.Log.Error("Forms database communication error.", ex, (object)this);
            }
            return output;
        }

        protected bool IsSitecore10(string connectionstring)
        {
            List<string> TableNames = new List<string>();

            using (SqlConnection connection = new SqlConnection(connectionstring))
            {
                connection.Open();
                var schema = connection.GetSchema("Tables");
                foreach (DataRow row in schema.Rows)
                {
                    TableNames.Add(row[2].ToString());
                }
                connection.Close();
            }

            if (TableNames.Any(x => x.Contains("FormEntries")))
            {
                return true;
            }
            return false;
        }

        protected void RunSqlCommand(string commandText, string entryId, string connectionstring)
        {
            using (SqlConnection connection = new SqlConnection(connectionstring))
            {
                SqlCommand command = new SqlCommand(commandText, connection);
                command.Parameters.Add("@EntryId", System.Data.SqlDbType.UniqueIdentifier);
                command.Parameters["@EntryId"].Value = new Guid(entryId);
                connection.Open();
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    var result = ParseResults(reader);
                }
                connection.Close();
            }
        }

        protected Dictionary<string, int> ParseResults(SqlDataReader reader)
        {
            var pollvalues = new Dictionary<string, int>();
            while (reader.Read())
            {
                string str = reader.GetString(0);
                int count = reader.GetInt32(1);
                if (!string.IsNullOrEmpty(str))
                    pollvalues.Add(str, count);
            }
            return pollvalues;
        }

        #region Http Response

        protected void StartResponse(string fileName)
        {
            Response.Clear();
            Response.Buffer = true;
            Response.AddHeader("content-disposition", string.Format("attachment;filename={0}.csv", fileName));
            Response.Charset = "";
            Response.ContentType = "text/csv";
            Response.ContentEncoding = System.Text.Encoding.UTF8;

        }

        protected void SetCookieAndResponse(string responseValue)
        {
            var downloadToken = txtDownloadToken.Value;
            var responseCookie = new HttpCookie("DownloadToken");
            responseCookie.Value = downloadToken;
            responseCookie.HttpOnly = false;
            responseCookie.Expires = DateTime.Now.AddDays(1);
            Response.Cookies.Add(responseCookie);
            Response.Output.Write(responseValue);
            Response.Flush();
            Response.End();
        }

        #endregion

        #region CsvParser

        public class CsvParser
        {
            private string delimiter = CultureInfo.CurrentCulture.TextInfo.ListSeparator;
            private char escape = '"';
            private char quote = '"';
            private string quoteString = "\"";
            private string doubleQuoteString = "\"\"";
            private char[] quoteRequiredChars;
            private CultureInfo cultureInfo = CultureInfo.CurrentCulture;
            private bool quoteAllFields;
            private bool quoteNoFields;
            private ReadingContext context;
            private IFieldReader fieldReader;
            private bool disposed;
            private int c = -1;

            public char[] InjectionCharacters
            {
                get { return new[] { '=', '@', '+', '-' }; }
            }

            public char InjectionEscapeCharacter
            {
                get { return '\t'; }
            }

            /// <summary>
            /// Gets the <see cref="FieldReader"/>.
            /// </summary>
            public IFieldReader FieldReader
            {
                get
                {
                    return fieldReader;
                }
            }

            /// <summary>
            /// Creates a new parser using the given <see cref="TextReader" />.
            /// </summary>
            /// <param name="reader">The <see cref="TextReader" /> with the CSV file data.</param>
            public CsvParser(TextReader reader) : this(new CsvFieldReader(reader, new Configuration(), false)) { }

            /// <summary>
            /// Creates a new parser using the given <see cref="TextReader" />.
            /// </summary>
            /// <param name="reader">The <see cref="TextReader" /> with the CSV file data.</param>
            /// <param name="leaveOpen">true to leave the reader open after the CsvReader object is disposed, otherwise false.</param>
            public CsvParser(TextReader reader, bool leaveOpen) : this(new CsvFieldReader(reader, new Configuration(), leaveOpen)) { }

            /// <summary>
            /// Creates a new parser using the given <see cref="TextReader"/> and <see cref="Configuration"/>.
            /// </summary>
            /// <param name="reader">The <see cref="TextReader"/> with the CSV file data.</param>
            /// <param name="configuration">The configuration.</param>
            public CsvParser(TextReader reader, Configuration configuration) : this(new CsvFieldReader(reader, configuration, false)) { }

            /// <summary>
            /// Creates a new parser using the given <see cref="TextReader"/> and <see cref="Configuration"/>.
            /// </summary>
            /// <param name="reader">The <see cref="TextReader"/> with the CSV file data.</param>
            /// <param name="configuration">The configuration.</param>
            /// <param name="leaveOpen">true to leave the reader open after the CsvReader object is disposed, otherwise false.</param>
            public CsvParser(TextReader reader, Configuration configuration, bool leaveOpen) : this(new CsvFieldReader(reader, configuration, leaveOpen)) { }

            /// <summary>
            /// Creates a new parser using the given <see cref="FieldReader"/>.
            /// </summary>
            /// <param name="fieldReader">The field reader.</param>
            public CsvParser(IFieldReader fieldReader)
            {
                this.fieldReader = fieldReader;
                context = fieldReader.Context as ReadingContext;
            }

            public List<string[]> GetRows()
            {
                // Don't forget about the async method below!
                List<string[]> rows = new List<string[]>();
                do
                {
                    context.Record = Read();
                    if (context.Record != null) rows.Add(context.Record);
                }
                while (context.Record != null);

                context.CurrentIndex = -1;
                context.HasBeenRead = true;

                return rows;
            }

            public string[] Read()
            {
                try
                {
                    var row = ReadLine();

                    return row;
                }
                catch (Exception ex)
                {
                    throw;
                }
            }

            private string[] ReadLine()
            {
                context.RecordBuilder.Clear();
                context.Row++;
                context.RawRow++;

                while (true)
                {
                    if (fieldReader.IsBufferEmpty && !fieldReader.FillBuffer())
                    {
                        // End of file.
                        if (context.RecordBuilder.Length > 0)
                        {
                            // There was no line break at the end of the file.
                            // We need to return the last record first.
                            context.RecordBuilder.Add(fieldReader.GetField());
                            return context.RecordBuilder.ToArray();
                        }

                        return null;
                    }

                    c = fieldReader.GetChar();

                    if (context.RecordBuilder.Length == 0 && ((c == context.ParserConfiguration.Comment && context.ParserConfiguration.AllowComments) || c == '\r' || c == '\n'))
                    {
                        ReadBlankLine();
                        if (!context.ParserConfiguration.IgnoreBlankLines)
                        {
                            break;
                        }

                        continue;
                    }

                    // Trim start outside of quotes.
                    if (c == ' ' && (context.ParserConfiguration.TrimOptions & TrimOptions.Trim) == TrimOptions.Trim)
                    {
                        ReadSpaces();
                        fieldReader.SetFieldStart(-1);
                    }

                    if (c == context.ParserConfiguration.Quote && !context.ParserConfiguration.IgnoreQuotes)
                    {
                        if (ReadQuotedField())
                        {
                            break;
                        }
                    }
                    else
                    {
                        if (ReadField())
                        {
                            break;
                        }
                    }
                }

                return context.RecordBuilder.ToArray();
            }

            protected virtual bool ReadQuotedField()
            {
                var inQuotes = true;
                var quoteCount = 1;
                // Set the start of the field to after the quote.
                fieldReader.SetFieldStart();

                while (true)
                {
                    var cPrev = c;

                    if (fieldReader.IsBufferEmpty && !fieldReader.FillBuffer())
                    {
                        // End of file.
                        fieldReader.SetFieldEnd();
                        context.RecordBuilder.Add(fieldReader.GetField());

                        return true;
                    }

                    c = fieldReader.GetChar();

                    // Trim start inside quotes.
                    if (quoteCount == 1 && c == ' ' && cPrev == context.ParserConfiguration.Quote && (context.ParserConfiguration.TrimOptions & TrimOptions.InsideQuotes) == TrimOptions.InsideQuotes)
                    {
                        ReadSpaces();
                        cPrev = ' ';
                        fieldReader.SetFieldStart(-1);
                    }

                    // Trim end inside quotes.
                    if (inQuotes && c == ' ' && (context.ParserConfiguration.TrimOptions & TrimOptions.InsideQuotes) == TrimOptions.InsideQuotes)
                    {
                        fieldReader.SetFieldEnd(-1);
                        fieldReader.AppendField();
                        fieldReader.SetFieldStart(-1);
                        ReadSpaces();
                        cPrev = ' ';

                        if (c == context.ParserConfiguration.Escape || c == context.ParserConfiguration.Quote)
                        {
                            inQuotes = !inQuotes;
                            quoteCount++;

                            cPrev = c;

                            if (fieldReader.IsBufferEmpty && !fieldReader.FillBuffer())
                            {
                                // End of file.
                                fieldReader.SetFieldStart();
                                fieldReader.SetFieldEnd();
                                context.RecordBuilder.Add(fieldReader.GetField());

                                return true;
                            }

                            c = fieldReader.GetChar();

                            if (c == context.ParserConfiguration.Quote)
                            {
                                // If we find a second quote, this isn't the end of the field.
                                // We need to keep the spaces in this case.

                                inQuotes = !inQuotes;
                                quoteCount++;

                                fieldReader.SetFieldEnd(-1);
                                fieldReader.AppendField();
                                fieldReader.SetFieldStart();

                                continue;
                            }
                            else
                            {
                                // If there isn't a second quote, this is the end of the field.
                                // We need to ignore the spaces.
                                fieldReader.SetFieldStart(-1);
                            }
                        }
                    }

                    if (inQuotes && c == context.ParserConfiguration.Escape || c == context.ParserConfiguration.Quote)
                    {
                        inQuotes = !inQuotes;
                        quoteCount++;

                        if (!inQuotes)
                        {
                            // Add an offset for the quote.
                            fieldReader.SetFieldEnd(-1);
                            fieldReader.AppendField();
                            fieldReader.SetFieldStart();
                        }

                        continue;
                    }

                    if (inQuotes)
                    {
                        if (c == '\r' || (c == '\n' && cPrev != '\r'))
                        {
                            if (context.ParserConfiguration.LineBreakInQuotedFieldIsBadData)
                            {
                                context.ParserConfiguration.BadDataFound.Invoke(context);
                            }

                            // Inside a quote \r\n is just another character to absorb.
                            context.RawRow++;
                        }
                    }

                    if (!inQuotes)
                    {
                        // Trim end outside of quotes.
                        if (c == ' ' && (context.ParserConfiguration.TrimOptions & TrimOptions.Trim) == TrimOptions.Trim)
                        {
                            ReadSpaces();
                            fieldReader.SetFieldStart(-1);
                        }

                        if (c == context.ParserConfiguration.Delimiter[0])
                        {
                            fieldReader.SetFieldEnd(-1);

                            if (ReadDelimiter())
                            {
                                // Add an extra offset because of the end quote.
                                context.RecordBuilder.Add(fieldReader.GetField());

                                return false;
                            }
                        }
                        else if (c == '\r' || c == '\n')
                        {
                            fieldReader.SetFieldEnd(-1);
                            var offset = ReadLineEnding();
                            fieldReader.SetRawRecordEnd(offset);
                            context.RecordBuilder.Add(fieldReader.GetField());

                            fieldReader.SetFieldStart(offset);
                            fieldReader.SetBufferPosition(offset);

                            return true;
                        }
                        else if (cPrev == context.ParserConfiguration.Quote)
                        {
                            // We're out of quotes. Read the reset of
                            // the field like a normal field.
                            return ReadField();
                        }
                    }
                }
            }

            protected virtual bool ReadField()
            {
                if (c != context.ParserConfiguration.Delimiter[0] && c != '\r' && c != '\n')
                {
                    if (fieldReader.IsBufferEmpty && !fieldReader.FillBuffer())
                    {
                        // End of file.
                        fieldReader.SetFieldEnd();

                        if (c == ' ' && (context.ParserConfiguration.TrimOptions & TrimOptions.Trim) == TrimOptions.Trim)
                        {
                            fieldReader.SetFieldStart();
                        }

                        context.RecordBuilder.Add(fieldReader.GetField());
                        return true;
                    }

                    c = fieldReader.GetChar();
                }

                var inSpaces = false;
                while (true)
                {
                    if (c == context.ParserConfiguration.Quote && !context.ParserConfiguration.IgnoreQuotes)
                    {
                        context.IsFieldBad = true;
                    }

                    // Trim end outside of quotes.
                    if (!inSpaces && c == ' ' && (context.ParserConfiguration.TrimOptions & TrimOptions.Trim) == TrimOptions.Trim)
                    {
                        inSpaces = true;
                        fieldReader.SetFieldEnd(-1);
                        fieldReader.AppendField();
                        fieldReader.SetFieldStart(-1);
                        fieldReader.SetRawRecordStart(-1);
                    }
                    else if (inSpaces && c != ' ')
                    {
                        // Hit a non-space char.
                        // Need to determine if it's the end of the field or another char.
                        inSpaces = false;
                        if (c == context.ParserConfiguration.Delimiter[0] || c == '\r' || c == '\n')
                        {
                            fieldReader.SetFieldStart(-1);
                        }
                    }

                    if (c == context.ParserConfiguration.Delimiter[0])
                    {
                        fieldReader.SetFieldEnd(-1);

                        // End of field.
                        if (ReadDelimiter())
                        {
                            // Set the end of the field to the char before the delimiter.
                            context.RecordBuilder.Add(fieldReader.GetField());

                            return false;
                        }
                    }
                    else if (c == '\r' || c == '\n')
                    {
                        // End of line.
                        fieldReader.SetFieldEnd(-1);
                        var offset = ReadLineEnding();
                        fieldReader.SetRawRecordEnd(offset);
                        context.RecordBuilder.Add(fieldReader.GetField());

                        fieldReader.SetFieldStart(offset);
                        fieldReader.SetBufferPosition(offset);

                        return true;
                    }

                    if (fieldReader.IsBufferEmpty && !fieldReader.FillBuffer())
                    {
                        // End of file.
                        fieldReader.SetFieldEnd();

                        if (c == ' ' && (context.ParserConfiguration.TrimOptions & TrimOptions.Trim) == TrimOptions.Trim)
                        {
                            fieldReader.SetFieldStart();
                        }

                        context.RecordBuilder.Add(fieldReader.GetField());

                        return true;
                    }

                    c = fieldReader.GetChar();
                }
            }

            protected virtual bool ReadDelimiter()
            {
                if (c != context.ParserConfiguration.Delimiter[0])
                {
                    throw new InvalidOperationException("Tried reading a delimiter when the first delimiter char didn't match the current char.");
                }

                if (context.ParserConfiguration.Delimiter.Length == 1)
                {
                    return true;
                }

                for (var i = 1; i < context.ParserConfiguration.Delimiter.Length; i++)
                {
                    if (fieldReader.IsBufferEmpty && !fieldReader.FillBuffer())
                    {
                        // End of file.
                        return false;
                    }

                    c = fieldReader.GetChar();
                    if (c != context.ParserConfiguration.Delimiter[i])
                    {
                        return false;
                    }
                }

                return true;
            }

            protected virtual bool ReadSpaces()
            {
                while (true)
                {
                    if (c != ' ')
                    {
                        break;
                    }

                    if (fieldReader.IsBufferEmpty && !fieldReader.FillBuffer())
                    {
                        // End of file.
                        return false;
                    }

                    c = fieldReader.GetChar();
                }

                return true;
            }

            protected virtual void ReadBlankLine()
            {
                if (context.ParserConfiguration.IgnoreBlankLines)
                {
                    context.Row++;
                }

                while (true)
                {
                    if (c == '\r' || c == '\n')
                    {
                        ReadLineEnding();
                        fieldReader.SetFieldStart();
                        return;
                    }

                    // If the buffer runs, it appends the current data to the field.
                    // We don't want to capture any data on a blank line, so we
                    // need to set the field start every char.
                    fieldReader.SetFieldStart();

                    if (fieldReader.IsBufferEmpty && !fieldReader.FillBuffer())
                    {
                        // End of file.
                        return;
                    }

                    c = fieldReader.GetChar();
                }
            }

            protected virtual int ReadLineEnding()
            {
                if (c != '\r' && c != '\n')
                {
                    throw new InvalidOperationException("Tried reading a line ending when the current char is not a \\r or \\n.");
                }

                var fieldStartOffset = 0;
                if (c == '\r')
                {
                    if (fieldReader.IsBufferEmpty && !fieldReader.FillBuffer())
                    {
                        // End of file.
                        return fieldStartOffset;
                    }

                    c = fieldReader.GetChar();
                    if (c != '\n' && c != -1)
                    {
                        // The start needs to be moved back.
                        fieldStartOffset--;
                    }
                }

                return fieldStartOffset;
            }

        }


        public partial class CsvFieldReader : IFieldReader
        {
            private ReadingContext context;
            private bool disposed;

            /// <summary>
            /// Gets the reading context.
            /// </summary>
            public ReadingContext Context
            {
                get
                {
                    return context;
                }
            }

            /// <summary>
            /// Gets a value indicating if the buffer is empty.
            /// True if the buffer is empty, otherwise false.
            /// </summary>
            public bool IsBufferEmpty
            {
                get { return context.BufferPosition >= context.CharsRead; }
            }

            /// <summary>
            /// Fills the buffer.
            /// </summary>
            /// <returns>True if there is more data left.
            /// False if all the data has been read.</returns>
            public bool FillBuffer()
            {
                try
                {
                    if (!IsBufferEmpty)
                    {
                        return false;
                    }

                    if (context.Buffer.Length == 0)
                    {
                        context.Buffer = new char[context.ParserConfiguration.BufferSize];
                    }

                    if (context.CharsRead > 0)
                    {
                        // Create a new buffer with extra room for what is left from
                        // the old buffer. Copy the remaining contents onto the new buffer.
                        var charactersUsed = Math.Min(context.FieldStartPosition, context.RawRecordStartPosition);
                        var bufferLeft = context.CharsRead - charactersUsed;
                        var bufferUsed = context.CharsRead - bufferLeft;
                        var tempBuffer = new char[bufferLeft + context.ParserConfiguration.BufferSize];
                        Array.Copy(context.Buffer, charactersUsed, tempBuffer, 0, bufferLeft);
                        context.Buffer = tempBuffer;

                        context.BufferPosition = context.BufferPosition - bufferUsed;
                        context.FieldStartPosition = context.FieldStartPosition - bufferUsed;
                        context.FieldEndPosition = Math.Max(context.FieldEndPosition - bufferUsed, 0);
                        context.RawRecordStartPosition = context.RawRecordStartPosition - bufferUsed;
                        context.RawRecordEndPosition = context.RawRecordEndPosition - bufferUsed;
                    }

                    context.CharsRead = context.Reader.Read(context.Buffer, context.BufferPosition,
                        context.ParserConfiguration.BufferSize);
                    if (context.CharsRead == 0)
                    {
                        // End of file
                        return false;
                    }

                    // Add the char count from the previous buffer that was copied onto this one.
                    context.CharsRead += context.BufferPosition;

                    return true;
                }
                catch
                {
                    return false;
                }
            }

            /// <summary>
            /// Fills the buffer.
            /// </summary>
            /// <returns>True if there is more data left.
            /// False if all the data has been read.</returns>
            public async Task<bool> FillBufferAsync()
            {
                if (!IsBufferEmpty)
                {
                    return false;
                }

                if (context.Buffer.Length == 0)
                {
                    context.Buffer = new char[context.ParserConfiguration.BufferSize];
                }

                if (context.CharsRead > 0)
                {
                    // Create a new buffer with extra room for what is left from
                    // the old buffer. Copy the remaining contents onto the new buffer.
                    var charactersUsed = Math.Min(context.FieldStartPosition, context.RawRecordStartPosition);
                    var bufferLeft = context.CharsRead - charactersUsed;
                    var bufferUsed = context.CharsRead - bufferLeft;
                    var tempBuffer = new char[bufferLeft + context.ParserConfiguration.BufferSize];
                    Array.Copy(context.Buffer, charactersUsed, tempBuffer, 0, bufferLeft);
                    context.Buffer = tempBuffer;

                    context.BufferPosition = context.BufferPosition - bufferUsed;
                    context.FieldStartPosition = context.FieldStartPosition - bufferUsed;
                    context.FieldEndPosition = Math.Max(context.FieldEndPosition - bufferUsed, 0);
                    context.RawRecordStartPosition = context.RawRecordStartPosition - bufferUsed;
                    context.RawRecordEndPosition = context.RawRecordEndPosition - bufferUsed;
                }

                context.CharsRead = await context.Reader.ReadAsync(context.Buffer, context.BufferPosition, context.ParserConfiguration.BufferSize).ConfigureAwait(false);
                if (context.CharsRead == 0)
                {
                    // End of file
                    return false;
                }

                // Add the char count from the previous buffer that was copied onto this one.
                context.CharsRead += context.BufferPosition;

                return true;
            }


            public CsvFieldReader(TextReader reader, Configuration configuration) : this(reader, configuration, false) { }


            public CsvFieldReader(TextReader reader, Configuration configuration, bool leaveOpen)
            {
                context = new ReadingContext(reader, configuration, leaveOpen);
            }

            /// <summary>
            /// Gets the next char as an <see cref="int"/>.
            /// </summary>
            public int GetChar()
            {
                var c = context.Buffer[context.BufferPosition];
                context.BufferPosition++;
                context.RawRecordEndPosition = context.BufferPosition;

                context.CharPosition++;

                return c;
            }

            /// <summary>
            /// Gets the field. This will append any reading progress.
            /// </summary>
            /// <returns>The current field.</returns>
            public string GetField()
            {
                AppendField();

                context.IsFieldBad = false;

                var result = context.FieldBuilder.ToString();
                context.FieldBuilder.Clear();

                return result;
            }

            /// <summary>
            /// Appends the current reading progress.
            /// </summary>
            public void AppendField()
            {
                context.RawRecordBuilder.Append(new string(context.Buffer, context.RawRecordStartPosition, context.RawRecordEndPosition - context.RawRecordStartPosition));
                context.RawRecordStartPosition = context.RawRecordEndPosition;

                var length = context.FieldEndPosition - context.FieldStartPosition;
                context.FieldBuilder.Append(new string(context.Buffer, context.FieldStartPosition, length));
                context.FieldStartPosition = context.BufferPosition;
                context.FieldEndPosition = 0;
            }

            /// <summary>
            /// Move's the buffer position according to the given offset.
            /// </summary>
            /// <param name="offset">The offset to move the buffer.</param>
            public void SetBufferPosition(int offset = 0)
            {
                var position = context.BufferPosition + offset;
                if (position >= 0)
                {
                    context.BufferPosition = position;
                }
            }

            /// <summary>
            /// Sets the start of the field to the current buffer position.
            /// </summary>
            /// <param name="offset">An offset for the field start.
            /// The offset should be less than 1.</param>
            public void SetFieldStart(int offset = 0)
            {
                var position = context.BufferPosition + offset;
                if (position >= 0)
                {
                    context.FieldStartPosition = position;
                }
            }

            /// <summary>
            /// Sets the end of the field to the current buffer position.
            /// </summary>
            /// <param name="offset">An offset for the field start.
            /// The offset should be less than 1.</param>
            public void SetFieldEnd(int offset = 0)
            {
                var position = context.BufferPosition + offset;
                if (position >= 0)
                {
                    context.FieldEndPosition = position;
                }
            }

            /// <summary>
            /// Sets the raw recodr start to the current buffer position;
            /// </summary>
            /// <param name="offset">An offset for the raw record start.
            /// The offset should be less than 1.</param>
            public void SetRawRecordStart(int offset)
            {
                var position = context.BufferPosition + offset;
                if (position >= 0)
                {
                    context.RawRecordStartPosition = position;
                }
            }

            /// <summary>
            /// Sets the raw record end to the current buffer position.
            /// </summary>
            /// <param name="offset">An offset for the raw record end.
            /// The offset should be less than 1.</param>
            public void SetRawRecordEnd(int offset)
            {
                var position = context.BufferPosition + offset;
                if (position >= 0)
                {
                    context.RawRecordEndPosition = position;
                }
            }

            /// <summary>
            /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
            /// </summary>
            /// <filterpriority>2</filterpriority>
            public void Dispose()
            {
                Dispose(true);
                GC.SuppressFinalize(this);
            }

            /// <summary>
            /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
            /// </summary>
            /// <param name="disposing">True if the instance needs to be disposed of.</param>
            protected virtual void Dispose(bool disposing)
            {
                if (disposed)
                {
                    return;
                }

                if (disposing)
                {
                    context.Dispose();
                }

                context = null;
                disposed = true;
            }
        }

        public class Configuration : IReaderConfiguration
        {
            private string delimiter = CultureInfo.CurrentCulture.TextInfo.ListSeparator;
            private char escape = '"';
            private char quote = '"';
            private string quoteString = "\"";
            private string doubleQuoteString = "\"\"";
            private char[] quoteRequiredChars;
            private CultureInfo cultureInfo = CultureInfo.CurrentCulture;
            private bool quoteAllFields;
            private bool quoteNoFields;

            /// <summary>
            /// Gets or sets a value indicating if the
            /// CSV file has a header record.
            /// Default is true.
            /// </summary>
            public bool HasHeaderRecord
            {
                get { return hasHeaderRecord; }
                set { hasHeaderRecord = value; }
            }

            private bool hasHeaderRecord = true;

            public Action<ReadingContext> BadDataFound { get; set; }

            /// <summary>
            /// Builds the values for the RequiredQuoteChars property.
            /// </summary>
            public Func<char[]> BuildRequiredQuoteChars { get; set; }

            /// <summary>
            /// Gets or sets a value indicating if a line break found in a quote field should
            /// be considered bad data. True to consider a line break bad data, otherwise false.
            /// Defaults to false.
            /// </summary>
            public bool LineBreakInQuotedFieldIsBadData { get; set; }

            /// <summary>
            /// Gets or sets a value indicating if fields should be sanitized
            /// to prevent malicious injection. This covers MS Excel, 
            /// Google Sheets and Open Office Calc.
            /// </summary>
            public bool SanitizeForInjection { get; set; }

            /// <summary>
            /// Gets or sets the characters that are used for injection attacks.
            /// </summary>
            public char[] InjectionCharacters
            {
                get
                {
                    return new[] { '=', '@', '+', '-' };
                }
            }

            /// <summary>
            /// Gets or sets the character used to escape a detected injection.
            /// </summary>
            public char InjectionEscapeCharacter
            {
                get
                {
                    return '\t';
                }
            }

            public Func<Type, string, string> ReferenceHeaderPrefix { get; set; }

            /// <summary>
            /// Gets or sets a value indicating whether changes in the column
            /// count should be detected. If true, a <see cref="BadDataException"/>
            /// will be thrown if a different column count is detected.
            /// </summary>
            /// <value>
            /// <c>true</c> if [detect column count changes]; otherwise, <c>false</c>.
            /// </value>
            public bool DetectColumnCountChanges { get; set; }

            public void UnregisterClassMap(Type classMapType)
            {
                throw new NotImplementedException();
            }

            public void UnregisterClassMap()
            {
                throw new NotImplementedException();
            }

            public Func<Type, bool> ShouldUseConstructorParameters { get; set; }

            /// <summary>
            /// Gets or sets a value indicating whether references
            /// should be ignored when auto mapping. True to ignore
            /// references, otherwise false. Default is false.
            /// </summary>
            public bool IgnoreReferences { get; set; }

            public Func<string[], bool> ShouldSkipRecord { get; set; }

            /// <summary>
            /// Gets or sets the field trimming options.
            /// </summary>
            public TrimOptions TrimOptions { get; set; }

            /// <summary>
            /// Gets or sets the delimiter used to separate fields.
            /// Default is CultureInfo.CurrentCulture.TextInfo.ListSeparator.
            /// </summary>
            public string Delimiter
            {
                get { return delimiter; }
                set
                {
                    if (value == "\n")
                    {
                        throw new ConfigurationException("Newline is not a valid delimiter.");
                    }

                    if (value == "\r")
                    {
                        throw new ConfigurationException("Carriage return is not a valid delimiter.");
                    }

                    if (value == System.Convert.ToString(quote))
                    {
                        throw new ConfigurationException("You can not use the quote as a delimiter.");
                    }

                    delimiter = value;

                    quoteRequiredChars = BuildRequiredQuoteChars();
                }
            }

            /// <summary>
            /// Gets or sets the escape character used to escape a quote inside a field.
            /// Default is '"'.
            /// </summary>
            public char Escape
            {
                get { return escape; }
                set
                {
                    if (value == '\n')
                    {
                        throw new ConfigurationException("Newline is not a valid escape.");
                    }

                    if (value == '\r')
                    {
                        throw new ConfigurationException("Carriage return is not a valid escape.");
                    }

                    if (value.ToString() == delimiter)
                    {
                        throw new ConfigurationException("You can not use the delimiter as an escape.");
                    }

                    escape = value;

                    doubleQuoteString = escape + quoteString;
                }
            }

            /// <summary>
            /// Gets or sets the character used to quote fields.
            /// Default is '"'.
            /// </summary>
            public char Quote
            {
                get { return quote; }
                set
                {
                    if (value == '\n')
                    {
                        throw new ConfigurationException("Newline is not a valid quote.");
                    }

                    if (value == '\r')
                    {
                        throw new ConfigurationException("Carriage return is not a valid quote.");
                    }

                    if (value == '\0')
                    {
                        throw new ConfigurationException("Null is not a valid quote.");
                    }

                    if (System.Convert.ToString(value) == delimiter)
                    {
                        throw new ConfigurationException("You can not use the delimiter as a quote.");
                    }

                    quote = value;

                    quoteString = System.Convert.ToString(value, cultureInfo);
                    doubleQuoteString = escape + quoteString;
                }
            }

            /// <summary>
            /// Gets a string representation of the currently configured Quote character.
            /// </summary>
            /// <value>
            /// The new quote string.
            /// </value>
            public string QuoteString
            {
                get
                {
                    return quoteString;
                }
            }

            /// <summary>
            /// Gets a string representation of two of the currently configured Quote characters.
            /// </summary>
            /// <value>
            /// The new double quote string.
            /// </value>
            public string DoubleQuoteString
            {
                get
                {
                    return doubleQuoteString;
                }
            }

            /// <summary>
            /// Gets an array characters that require
            /// the field to be quoted.
            /// </summary>
            public char[] QuoteRequiredChars
            {
                get
                {
                    return quoteRequiredChars;
                }
            }

            /// <summary>
            /// Gets or sets the character used to denote
            /// a line that is commented out. Default is '#'.
            /// </summary>
            public char Comment
            {
                get { return comment; }
                set { comment = value; }
            }

            private char comment = '#';

            /// <summary>
            /// Gets or sets a value indicating if comments are allowed.
            /// True to allow commented out lines, otherwise false.
            /// </summary>
            public bool AllowComments { get; set; }

            /// <summary>
            /// Gets or sets the size of the buffer
            /// used for reading CSV files.
            /// Default is 2048.
            /// </summary>
            public int BufferSize
            {
                get { return bufferSize; }
                set { bufferSize = value; }
            }
            private int bufferSize = 2048;

            /// <summary>
            /// Gets or sets a value indicating whether all fields are quoted when writing,
            /// or just ones that have to be. <see cref="QuoteAllFields"/> and
            /// <see cref="QuoteNoFields"/> cannot be true at the same time. Turning one
            /// on will turn the other off.
            /// </summary>
            /// <value>
            ///   <c>true</c> if all fields should be quoted; otherwise, <c>false</c>.
            /// </value>
            public bool QuoteAllFields
            {
                get { return quoteAllFields; }
                set
                {
                    quoteAllFields = value;
                    if (quoteAllFields && quoteNoFields)
                    {
                        // Both can't be true at the same time.
                        quoteNoFields = false;
                    }
                }
            }

            /// <summary>
            /// Gets or sets a value indicating whether no fields are quoted when writing.
            /// <see cref="QuoteAllFields"/> and <see cref="QuoteNoFields"/> cannot be true 
            /// at the same time. Turning one on will turn the other off.
            /// </summary>
            /// <value>
            ///   <c>true</c> if [quote no fields]; otherwise, <c>false</c>.
            /// </value>
            public bool QuoteNoFields
            {
                get { return quoteNoFields; }
                set
                {
                    quoteNoFields = value;
                    if (quoteNoFields && quoteAllFields)
                    {
                        // Both can't be true at the same time.
                        quoteAllFields = false;
                    }
                }
            }

            /// <summary>
            /// Gets or sets a value indicating whether the number of bytes should
            /// be counted while parsing. Default is false. This will slow down parsing
            /// because it needs to get the byte count of every char for the given encoding.
            /// The <see cref="Encoding"/> needs to be set correctly for this to be accurate.
            /// </summary>
            public bool CountBytes { get; set; }

            /// <summary>
            /// Gets or sets the encoding used when counting bytes.
            /// </summary>
            public Encoding Encoding
            {
                get { return encoding; }
                set { encoding = value; }
            }

            private Encoding encoding = Encoding.UTF8;

            /// <summary>
            /// Gets or sets the culture info used to read an write CSV files.
            /// </summary>
            public CultureInfo CultureInfo
            {
                get { return cultureInfo; }
                set { cultureInfo = value; }
            }

            public Func<string, int, string> PrepareHeaderForMatch { get; set; }

            /// <summary>
            /// Gets or sets a value indicating if quotes should be
            /// ignored when parsing and treated like any other character.
            /// </summary>
            public bool IgnoreQuotes { get; set; }

            /// <summary>
            /// Gets or sets a value indicating if private
            /// member should be read from and written to.
            /// True to include private member, otherwise false. Default is false.
            /// </summary>
            public bool IncludePrivateMembers { get; set; }

            /// <summary>
            /// Gets or sets a value indicating if blank lines
            /// should be ignored when reading.
            /// True to ignore, otherwise false. Default is true.
            /// </summary>
            public bool IgnoreBlankLines
            {
                get { return ignoreBlankLines; }
                set { ignoreBlankLines = value; }
            }

            private bool ignoreBlankLines = true;
        }

        public interface IFieldReader : IDisposable
        {
            /// <summary>
            /// Gets the reading context.
            /// </summary>
            ReadingContext Context { get; }

            /// <summary>
            /// Gets a value indicating if the buffer is empty.
            /// True if the buffer is empty, otherwise false.
            /// </summary>
            bool IsBufferEmpty { get; }

            /// <summary>
            /// Fills the buffer.
            /// </summary>
            /// <returns>True if there is more data left.
            /// False if all the data has been read.</returns>
            bool FillBuffer();

            /// <summary>
            /// Fills the buffer asynchronously.
            /// </summary>
            /// <returns>True if there is more data left.
            /// False if all the data has been read.</returns>
            Task<bool> FillBufferAsync();

            /// <summary>
            /// Gets the next char as an <see cref="int"/>.
            /// </summary>
            int GetChar();

            /// <summary>
            /// Gets the field. This will append any reading progress.
            /// </summary>
            /// <returns>The current field.</returns>
            string GetField();

            /// <summary>
            /// Appends the current reading progress.
            /// </summary>
            void AppendField();

            /// <summary>
            /// Move's the buffer position according to the given offset.
            /// </summary>
            /// <param name="offset">The offset to move the buffer.</param>
            void SetBufferPosition(int offset = 0);

            /// <summary>
            /// Sets the start of the field to the current buffer position.
            /// </summary>
            /// <param name="offset">An offset for the field start.
            /// The offset should be less than 1.</param>
            void SetFieldStart(int offset = 0);

            /// <summary>
            /// Sets the end of the field to the current buffer position.
            /// </summary>
            /// <param name="offset">An offset for the field start.
            /// The offset should be less than 1.</param>
            void SetFieldEnd(int offset = 0);

            /// <summary>
            /// Sets the raw recodr start to the current buffer position;
            /// </summary>
            /// <param name="offset">An offset for the raw record start.
            /// The offset should be less than 1.</param>
            void SetRawRecordStart(int offset);

            /// <summary>
            /// Sets the raw record end to the current buffer position.
            /// </summary>
            /// <param name="offset">An offset for the raw record end.
            /// The offset should be less than 1.</param>
            void SetRawRecordEnd(int offset);
        }

        public class RecordBuilder
        {
            private const int DEFAULT_CAPACITY = 16;
            private string[] record;
            private int position;
            private int capacity;

            /// <summary>
            /// The number of records.
            /// </summary>
            public int Length
            {
                get
                {
                    return position;
                }
            }

            /// <summary>
            /// The total record capacity.
            /// </summary>
            public int Capacity
            {
                get
                {
                    return capacity;
                }
            }

            /// <summary>
            /// Creates a new <see cref="RecordBuilder"/> using defaults.
            /// </summary>
            public RecordBuilder() : this(DEFAULT_CAPACITY) { }

            /// <summary>
            /// Creatse a new <see cref="RecordBuilder"/> using the given capacity.
            /// </summary>
            /// <param name="capacity">The initial capacity.</param>
            public RecordBuilder(int capacity)
            {
                this.capacity = capacity > 0 ? capacity : DEFAULT_CAPACITY;

                record = new string[capacity];
            }

            /// <summary>
            /// Adds a new field to the <see cref="RecordBuilder"/>.
            /// </summary>
            /// <param name="field">The field to add.</param>
            /// <returns>The current instance of the <see cref="RecordBuilder"/>.</returns>
            public RecordBuilder Add(string field)
            {
                if (position == record.Length)
                {
                    capacity = capacity * 2;
                    Array.Resize(ref record, capacity);
                }

                record[position] = field;
                position++;

                return this;
            }

            /// <summary>
            /// Clears the records.
            /// </summary>
            /// <returns>The current instance of the <see cref="RecordBuilder"/>.</returns>
            public RecordBuilder Clear()
            {
                position = 0;

                return this;
            }

            /// <summary>
            /// Returns the record as an <see cref="T:string[]"/>.
            /// </summary>
            /// <returns>The record as an <see cref="T:string[]"/>.</returns>
            public string[] ToArray()
            {
                var array = new string[position];
                Array.Copy(record, array, position);

                return array;
            }
        }

        public class ReadingContext : IDisposable
        {
            private bool disposed;
            private readonly Configuration configuration;

            /// <summary>
            /// Gets the raw record builder.
            /// </summary>
            public StringBuilder RawRecordBuilder = new StringBuilder();

            /// <summary>
            /// Gets the field builder.
            /// </summary>
            public StringBuilder FieldBuilder = new StringBuilder();

            public IParserConfiguration ParserConfiguration
            {
                get
                {
                    return configuration;
                }
            }


            /// <summary>
            /// Gets the named indexes.
            /// </summary>
            public Dictionary<string, List<int>> NamedIndexes = new Dictionary<string, List<int>>();


            /// <summary>
            /// Gets the create record functions.
            /// </summary>
            public Dictionary<Type, Delegate> CreateRecordFuncs = new Dictionary<Type, Delegate>();

            /// <summary>
            /// Gets the hydrate record actions.
            /// </summary>
            public Dictionary<Type, Delegate> HydrateRecordActions = new Dictionary<Type, Delegate>();


            /// <summary>
            /// Gets the <see cref="TextReader"/> that is read from.
            /// </summary>
            public TextReader Reader;

            /// <summary>
            /// Gets a value indicating if the <see cref="Reader"/>
            /// should be left open when disposing.
            /// </summary>
            public bool LeaveOpen;

            /// <summary>
            /// Gets the buffer used to store data from the <see cref="Reader"/>.
            /// </summary>
            public char[] Buffer;

            /// <summary>
            /// Gets the buffer position.
            /// </summary>
            public int BufferPosition;

            /// <summary>
            /// Gets the field start position.
            /// </summary>
            public int FieldStartPosition;

            /// <summary>
            /// Gets the field end position.
            /// </summary>
            public int FieldEndPosition;

            /// <summary>
            /// Gets the raw record start position.
            /// </summary>
            public int RawRecordStartPosition;

            /// <summary>
            /// Gets the raw record end position.
            /// </summary>
            public int RawRecordEndPosition;

            /// <summary>
            /// Gets the number of characters read from the <see cref="Reader"/>.
            /// </summary>
            public int CharsRead;

            /// <summary>
            /// Gets the character position.
            /// </summary>
            public long CharPosition;

            /// <summary>
            /// Gets the byte position.
            /// </summary>
            public long BytePosition;

            /// <summary>
            /// Gets a value indicating if the field is bad.
            /// True if the field is bad, otherwise false.
            /// A field is bad if a quote is found in a field
            /// that isn't escaped.
            /// </summary>
            public bool IsFieldBad;

            /// <summary>
            /// Gets the record.
            /// </summary>
            public string[] Record;

            /// <summary>
            /// Gets the row of the CSV file that the parser is currently on.
            /// </summary>
            public int Row;

            /// <summary>
            /// Gets the row of the CSV file that the parser is currently on.
            /// This is the actual file row.
            /// </summary>
            public int RawRow;

            /// <summary>
            /// Gets a value indicating if reading has begun.
            /// </summary>
            public bool HasBeenRead;

            /// <summary>
            /// Gets the header record.
            /// </summary>
            public string[] HeaderRecord;

            /// <summary>
            /// Gets the current index.
            /// </summary>
            public int CurrentIndex = -1;

            /// <summary>
            /// Gets the column count.
            /// </summary>
            public int ColumnCount;

            /// <summary>
            /// Gets all the characters of the record including
            /// quotes, delimeters, and line endings.
            /// </summary>
            public string RawRecord
            {
                get
                {
                    return RawRecordBuilder.ToString();
                }
            }

            /// <summary>
            /// Gets the field.
            /// </summary>
            public string Field
            {
                get
                {
                    return FieldBuilder.ToString();
                }
            }

            public RecordBuilder RecordBuilder = new RecordBuilder();

            /// <summary>
            /// Initializes a new instance.
            /// </summary>
            /// <param name="reader">The reader.</param>
            /// <param name="configuration">The configuration.</param>
            /// <param name="leaveOpen">A value indicating if the TextReader should be left open when disposing.</param>
            public ReadingContext(TextReader reader, Configuration configuration, bool leaveOpen)
            {
                Reader = reader;
                this.configuration = configuration;
                LeaveOpen = leaveOpen;
                Buffer = new char[0];
            }

            /// <summary>
            /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
            /// </summary>
            /// <filterpriority>2</filterpriority>
            public void Dispose()
            {
                Dispose(true);
                GC.SuppressFinalize(this);
            }

            /// <summary>
            /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
            /// </summary>
            /// <param name="disposing">True if the instance needs to be disposed of.</param>
            protected virtual void Dispose(bool disposing)
            {
                if (disposed)
                {
                    return;
                }

                if (disposing)
                {
                    Reader.Dispose();
                }

                Reader = null;
                disposed = true;
            }
        }

        public interface IParserConfiguration
        {
            /// <summary>
            /// Gets or sets the size of the buffer
            /// used for reading CSV files.
            /// Default is 2048.
            /// </summary>
            int BufferSize { get; set; }

            /// <summary>
            /// Gets or sets a value indicating whether the number of bytes should
            /// be counted while parsing. Default is false. This will slow down parsing
            /// because it needs to get the byte count of every char for the given encoding.
            /// The <see cref="Encoding"/> needs to be set correctly for this to be accurate.
            /// </summary>
            bool CountBytes { get; set; }

            /// <summary>
            /// Gets or sets the encoding used when counting bytes.
            /// </summary>
            Encoding Encoding { get; set; }

            /// <summary>
            /// Gets or sets the function that is called when bad field data is found. A field
            /// has bad data if it contains a quote and the field is not quoted (escaped).
            /// You can supply your own function to do other things like logging the issue
            /// instead of throwing an exception.
            /// Arguments: context
            /// </summary>
            Action<ReadingContext> BadDataFound { get; set; }

            /// <summary>
            /// Gets or sets a value indicating if a line break found in a quote field should
            /// be considered bad data. True to consider a line break bad data, otherwise false.
            /// Defaults to false.
            /// </summary>
            bool LineBreakInQuotedFieldIsBadData { get; set; }

            /// <summary>
            /// Gets or sets the character used to denote
            /// a line that is commented out. Default is '#'.
            /// </summary>
            char Comment { get; set; }

            /// <summary>
            /// Gets or sets a value indicating if comments are allowed.
            /// True to allow commented out lines, otherwise false.
            /// </summary>
            bool AllowComments { get; set; }

            /// <summary>
            /// Gets or sets a value indicating if blank lines
            /// should be ignored when reading.
            /// True to ignore, otherwise false. Default is true.
            /// </summary>
            bool IgnoreBlankLines { get; set; }

            /// <summary>
            /// Gets or sets a value indicating if quotes should be
            /// ingored when parsing and treated like any other character.
            /// </summary>
            bool IgnoreQuotes { get; set; }

            /// <summary>
            /// Gets or sets the character used to quote fields.
            /// Default is '"'.
            /// </summary>
            char Quote { get; set; }

            /// <summary>
            /// Gets or sets the delimiter used to separate fields.
            /// Default is CultureInfo.CurrentCulture.TextInfo.ListSeparator.
            /// </summary>
            string Delimiter { get; set; }

            /// <summary>
            /// Gets or sets the escape character used to escape a quote inside a field.
            /// Default is '"'.
            /// </summary>
            char Escape { get; set; }

            /// <summary>
            /// Gets or sets the field trimming options.
            /// </summary>
            TrimOptions TrimOptions { get; set; }
        }

        [Flags]
        public enum TrimOptions
        {
            /// <summary>
            /// No trimming.
            /// </summary>
            None = 0,

            /// <summary>
            /// Trims the whitespace around a field.
            /// </summary>
            Trim = 1,

            /// <summary>
            /// Trims the whitespace inside of quotes around a field.
            /// </summary>
            InsideQuotes = 2
        }

        public interface IReaderConfiguration : IParserConfiguration
        {
            /// <summary>
            /// Gets or sets a value indicating if the
            /// CSV file has a header record.
            /// Default is true.
            /// </summary>
            bool HasHeaderRecord { get; set; }


            /// <summary>
            /// Gets or sets the culture info used to read an write CSV files.
            /// </summary>
            CultureInfo CultureInfo { get; set; }

            /// <summary>
            /// Prepares the header field for matching against a member name.
            /// The header field and the member name are both ran through this function.
            /// You should do things like trimming, removing whitespace, removing underscores,
            /// and making casing changes to ignore case.
            /// </summary>
            Func<string, int, string> PrepareHeaderForMatch { get; set; }

            /// <summary>
            /// Determines if constructor parameters should be used to create
            /// the class instead of the default constructor and members.
            /// </summary>
            Func<Type, bool> ShouldUseConstructorParameters { get; set; }

            /// <summary>
            /// Gets or sets a value indicating whether references
            /// should be ignored when auto mapping. True to ignore
            /// references, otherwise false. Default is false.
            /// </summary>
            bool IgnoreReferences { get; set; }

            /// <summary>
            /// Gets or sets the callback that will be called to
            /// determine whether to skip the given record or not.
            /// </summary>
            Func<string[], bool> ShouldSkipRecord { get; set; }

            /// <summary>
            /// Gets or sets a value indicating if private
            /// member should be read from and written to.
            /// True to include private member, otherwise false. Default is false.
            /// </summary>
            bool IncludePrivateMembers { get; set; }

            /// <summary>
            /// Gets or sets a callback that will return the prefix for a reference header.
            /// Arguments: memberType, memberName
            /// </summary>
            Func<Type, string, string> ReferenceHeaderPrefix { get; set; }

            /// <summary>
            /// Gets or sets a value indicating whether changes in the column
            /// count should be detected. If true, a <see cref="BadDataException"/>
            /// will be thrown if a different column count is detected.
            /// </summary>
            /// <value>
            /// <c>true</c> if [detect column count changes]; otherwise, <c>false</c>.
            /// </value>
            bool DetectColumnCountChanges { get; set; }

            /// <summary>
            /// Unregisters the class map.
            /// </summary>
            /// <param name="classMapType">The map type to unregister.</param>
            void UnregisterClassMap(Type classMapType);

            /// <summary>
            /// Unregisters all class maps.
            /// </summary>
            void UnregisterClassMap();
        }

        [Serializable]
        public class ConfigurationException : Exception
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="ConfigurationException"/> class.
            /// </summary>
            public ConfigurationException() { }

            /// <summary>
            /// Initializes a new instance of the <see cref="ConfigurationException"/> class
            /// with a specified error message.
            /// </summary>
            /// <param name="message">The message that describes the error.</param>
            public ConfigurationException(string message) : base(message) { }

            /// <summary>
            /// Initializes a new instance of the <see cref="ConfigurationException"/> class
            /// with a specified error message and a reference to the inner exception that 
            /// is the cause of this exception.
            /// </summary>
            /// <param name="message">The error message that explains the reason for the exception.</param>
            /// <param name="innerException">The exception that is the cause of the current exception, or a null reference (Nothing in Visual Basic) if no inner exception is specified.</param>
            public ConfigurationException(string message, Exception innerException) : base(message, innerException) { }
        }

        #endregion

       
    }
}