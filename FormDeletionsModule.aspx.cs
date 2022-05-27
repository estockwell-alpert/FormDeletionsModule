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

namespace ContentExportTool
{
    public partial class FormDeletionsModule : Page
    {
        protected void Page_Load(object sender, EventArgs e)
        {
            litFeedback.Text = String.Empty;

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
                var forms = formsFolder.Children.Where(x => x.TemplateName == "Form");
                ddForms.DataSource = forms;
                ddForms.DataTextField = "Name";
                ddForms.DataValueField = "ID";
                ddForms.DataBind();
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
            Guid formId;
            if (String.IsNullOrEmpty(ddForms.SelectedValue) || !Guid.TryParse(ddForms.SelectedValue, out formId))
            {
                litFormsResponse.Text = "You must enter a valid form ID";
                return;
            }

            var _formDataProvider = Sitecore.DependencyInjection.ServiceLocator.ServiceProvider.GetService(typeof(IFormDataProvider)) as IFormDataProvider;
            var data = _formDataProvider.GetEntries(formId, null, null);

            if (data == null || !data.Any())
            {
                litFormsResponse.Text = "No entries found for the given form ID";
                return;
            }

            StartResponse("Form Data - ");
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
                        itemLine += "\"" + entryField.Value + "\",";
                    }

                    sw.WriteLine(itemLine);
                }
                SetCookieAndResponse(sw.ToString());
            }
        }

        protected void btnDeleteFormEntries_Click(object sender, EventArgs e)
        {
            var output = "";

            var file = uplFormDelete.PostedFile;
            if (file == null || String.IsNullOrEmpty(file.FileName))
            {
                litFormsResponse.Text = "You must select a file first<br/>";
                return;
            }

            string extension = System.IO.Path.GetExtension(file.FileName);
            if (extension.ToLower() != ".csv")
            {
                litFormsResponse.Text = "Upload file must be in CSV format<br/>";
                return;
            }

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

            if (String.IsNullOrEmpty(formsConnectionString))
            {
                litFormsResponse.Text = "Could not find experienceforms connectionstring";
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
                        if (!(delete == "1" || bool.TryParse(delete, out var shouldDelete)))
                        {
                            continue;
                        }

                        var entryId = cells[formEntryId];

                        string commandText = @"DELETE from [FieldData] WHERE FormEntryId = @EntryId";


                        // delete from field data table
                        try
                        {
                            using (SqlConnection connection = new SqlConnection(formsConnectionString))
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
                        catch (Exception ex)
                        {
                            litFormsResponse.Text += ex.ToString();
                            Sitecore.Diagnostics.Log.Error("Forms database communication error.", ex, (object)this);
                        }


                        // delete from entries table
                        string commandTextEntries = @"DELETE from [FormEntry] WHERE ID = @EntryId";
                        try
                        {
                            using (SqlConnection connection = new SqlConnection(formsConnectionString))
                            {
                                SqlCommand command = new SqlCommand(commandTextEntries, connection);
                                command.Parameters.Add("@EntryId", System.Data.SqlDbType.UniqueIdentifier);
                                command.Parameters["@EntryId"].Value = new Guid(entryId);
                                connection.Open();
                                using (SqlDataReader reader = command.ExecuteReader())
                                {
                                    var result = ParseResults(reader);
                                }
                                connection.Close();

                                entriesDelete++;
                            }
                        }
                        catch (Exception ex)
                        {
                            commandTextEntries = @"DELETE from [FormEntries] WHERE ID = @EntryId";
                            try
                            {
                                using (SqlConnection connection = new SqlConnection(formsConnectionString))
                                {
                                    SqlCommand command = new SqlCommand(commandTextEntries, connection);
                                    command.Parameters.Add("@EntryId", System.Data.SqlDbType.UniqueIdentifier);
                                    command.Parameters["@EntryId"].Value = new Guid(entryId);
                                    connection.Open();
                                    using (SqlDataReader reader = command.ExecuteReader())
                                    {
                                        var result = ParseResults(reader);
                                    }
                                    connection.Close();

                                    entriesDelete++;
                                }
                            }
                            catch (Exception ex2)
                            {
                                litFormsResponse.Text += ex.ToString();
                                Sitecore.Diagnostics.Log.Error("Forms database communication error.", ex, (object)this);
                            }
                        }

                    }
                }
            }

            litFormsResponse.Text = "Deleted " + entriesDelete + " entries";
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
    }
}