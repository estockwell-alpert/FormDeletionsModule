# FormDeletionsModule

This module allows the user to delete specific entries for a given Sitecore form.

This module is for **Sitecore Forms**; it will not work with WFFM.

Tested in Sitecore 9.2.0; other versions coming soon.

*Note* The Export currently works in 10.0, but the Delete feature does not work because the Database Tables have different names in 10.0. This compatibility will be coming soon.

**Dependent on the following libraries:**
System;
System.Collections.Generic;
System.IO;
System.Linq;
System.Web;
System.Web.UI;
Sitecore.Data.Managers;
Sitecore.ExperienceForms.Data;
System.Configuration;
System.Data.SqlClient;
Sitecore.Sites;
System.Text;
System.Globalization;
System.Threading.Tasks;

# Installation

Download the latest package from the Releases page and install using the Sitecore Installation Wizard. On first installation you can ignore any existing files; if overwriting with a newer version, make sure to overwrite all FormDeletionsModule files

