using System;
using System.Web.UI;
using Sitecore;
using Sitecore.Install;
using Sitecore.Install.Items;
using Sitecore.Install.Zip;
using Sitecore.Configuration;
using Sitecore.Install.Files;
using System.Net;

namespace ContentExportTool
{
    public partial class ContentExportAdminPage : Page
    {
        protected void btnGeneratePackage_OnClick(object sender, EventArgs e)
        {
            var contentExportUtil = new ContentExport();
            var packageProject = new PackageProject()
            {
                Metadata =
                    {
                        PackageName = String.IsNullOrEmpty(txtFileName.Value) ? "Sitecore Form Deletion Module " + DateTime.Now.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture) : txtFileName.Value,
                        Author = "Erica Stockwell-Alpert",
                        Version = txtVersion.Value
                    }
            };

            packageProject.Sources.Clear();
            var source = new ExplicitItemSource();
            source.Name = "Items";

            var _core = Factory.GetDatabase("core");
            var _master = Factory.GetDatabase("master");

            // items
            var coreAppItem = _core.Items.GetItem("/sitecore/content/Applications/Form Deletion Module");
            var coreMenuItem =
                _core.Items.GetItem("/sitecore/content/Documents and settings/All users/Start menu/Right/Form Deletion Module");

            source.Entries.Add(new ItemReference(coreAppItem.Uri, false).ToString());
            source.Entries.Add(new ItemReference(coreMenuItem.Uri, false).ToString());

            packageProject.Sources.Add(source);

            // files
            var fileSource = new ExplicitFileSource();
            fileSource.Name = "Files";

            fileSource.Entries.Add(MainUtil.MapPath("C:\\Dev\\BayerCA\\Website\\sitecore\\shell\\Applications\\ContentExport\\FormDeletionsModule.aspx"));
            fileSource.Entries.Add(MainUtil.MapPath("C:\\Dev\\BayerCA\\Website\\sitecore\\shell\\Applications\\ContentExport\\FormDeletionsModule.aspx.cs"));
            fileSource.Entries.Add(MainUtil.MapPath("C:\\Dev\\BayerCA\\Website\\sitecore\\shell\\Applications\\ContentExport\\FormDeletionsModule.aspx.designer.cs"));
            fileSource.Entries.Add(MainUtil.MapPath("C:\\Dev\\BayerCA\\Website\\sitecore\\shell\\Applications\\ContentExport\\jquery-2.2.4.min.js"));
            fileSource.Entries.Add(MainUtil.MapPath("C:\\Dev\\BayerCA\\Website\\sitecore\\shell\\Applications\\ContentExport\\jquery-ui.min.js"));
            fileSource.Entries.Add(MainUtil.MapPath("C:\\Dev\\BayerCA\\Website\\sitecore\\shell\\Applications\\ContentExport\\ContentExportScripts.js"));
            fileSource.Entries.Add(
                MainUtil.MapPath("C:\\Dev\\BayerCA\\Website\\temp\\IconCache\\Network\\16x16\\download.png"));
            fileSource.Entries.Add(
                MainUtil.MapPath("C:\\Dev\\BayerCA\\Website\\temp\\IconCache\\Network\\32x32\\download.png"));
            fileSource.Entries.Add(
                MainUtil.MapPath("C:\\Dev\\BayerCA\\Website\\temp\\IconCache\\Network\\24x24\\download.png"));
            fileSource.Entries.Add(MainUtil.MapPath("C:\\Dev\\BayerCA\\Website\\sitecore\\shell\\Themes\\Standard\\Images\\ProgressIndicator\\sc-spinner32.gif"));

            packageProject.Sources.Add(fileSource);

            packageProject.SaveProject = true;

            var fileName = packageProject.Metadata.PackageName + ".zip";
            var filePath = contentExportUtil.FullPackageProjectPath(fileName);

            using (var writer = new PackageWriter(filePath))
            {
                Sitecore.Context.SetActiveSite("shell");
                writer.Initialize(Installer.CreateInstallationContext());
                PackageGenerator.GeneratePackage(packageProject, writer);
                Sitecore.Context.SetActiveSite("website");

                Response.Clear();
                Response.Buffer = true;
                Response.AddHeader("content-disposition", string.Format("attachment;filename={0}", fileName));
                Response.ContentType = "application/zip";

                byte[] data = new WebClient().DownloadData(filePath);
                Response.BinaryWrite(data);
                Response.Flush();
                Response.End();
            }
        }
    }
}
