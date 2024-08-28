using System;
using System.Collections.Generic;
using System.Net;
using System.IO;
using System.Linq;
using MySql.Data.MySqlClient;
using System.Web;
using System.Xml;
using System.Text;

class Program
{
    static string url2db(string url)
    {
        return url.Replace(".", "").Replace("wikipedia", "wiki");
    }
    static void Sendresponse(string type, string project, string source, int notless, string result)
    {
        string template = new StreamReader(Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "page-authors.html")).ReadToEnd();
        if (type == "cat")
            template = template.Replace("%checked_cat%", "checked");
        else if (type == "tmplt")
            template = template.Replace("%checked_tmplt%", "checked");
        else if (type == "talktmplt")
            template = template.Replace("%checked_talktmplt%", "checked");
        else if (type == "links")
            template = template.Replace("%checked_links%", "checked");
        else if (type == "talkcat")
            template = template.Replace("%checked_talkcat%", "checked");
        Console.WriteLine(template.Replace("%result%", result).Replace("%source%", source).Replace("%wiki%", project).Replace("%notless%", notless.ToString()));
    }
    static void Main()
    {
        var cl = new WebClient();
        var srcpages = new List<string>();
        string input = Environment.GetEnvironmentVariable("QUERY_STRING");
        if (input == "" || input == null)
        {
            Sendresponse("cat", "ru.wikipedia", "", 2, "");
            return;
        }
        var parameters = HttpUtility.ParseQueryString(input);

        string type = parameters[0];
        string project = parameters[1];
        var rawsource = parameters[2];
        var source = rawsource.Replace(" ", "_").Replace("\u200E", "").Replace("\r", "").Split('\n');//удаляем пробел нулевой ширины
        foreach (var s in source)
        {
            string upcased = char.ToUpper(s[0]) + s.Substring(1);
            if (!srcpages.Contains(upcased))
                srcpages.Add(upcased);
        }
        int notless = Convert.ToInt32(parameters[3]);
        string result = "";
        var pageids = new HashSet<int>();
        var pagenames = new HashSet<string>();
        var stats = new Dictionary<string, int>();
        var connect = new MySqlConnection(Environment.GetEnvironmentVariable("CONN_STRING").Replace("%project%", url2db(project)));
        connect.Open();
        MySqlCommand command;
        MySqlDataReader r;
        int c = 0;
        result = "<table border=\"1\" cellspacing=\"0\"><tr><th>№</th><th>Участник</th><th>Создал страниц</th></tr>\n";
        if (type == "cat")
            foreach (var s in srcpages)
            {
                command = new MySqlCommand("select cl_from from categorylinks where cl_to=\"" + s.Replace(" ", "_") + "\";", connect) { CommandTimeout = 99999 };
                r = command.ExecuteReader();
                while (r.Read())
                    if (!pageids.Contains(r.GetInt32(0)))
                        pageids.Add(r.GetInt32(0));
                r.Close();
            }
        else if (type == "tmplt")
            foreach (var s in srcpages)
            {
                command = new MySqlCommand("select tl_from from templatelinks join linktarget on lt_id=tl_target_id where lt_title=\"" + s + "\" and lt_namespace=10;", connect) { CommandTimeout = 99999 };
                r = command.ExecuteReader();
                while (r.Read())
                    if (!pageids.Contains(r.GetInt32(0)))
                        pageids.Add(r.GetInt32(0));
                r.Close();
            }
        else if (type == "talktmplt")
            foreach (var s in srcpages)
            {
                command = new MySqlCommand("select cast(page_title as char) title from templatelinks join page on page_id=tl_from join linktarget on lt_id=tl_target_id where lt_title=\"" + s + "\" and lt_namespace=10;", connect) { CommandTimeout = 99999 };
                r = command.ExecuteReader();
                while (r.Read())
                    if (!pagenames.Contains(r.GetString(0)))
                        pagenames.Add(r.GetString(0));
                r.Close();
            }
        else if (type == "talkcat")
            foreach (var s in srcpages)
            {
                command = new MySqlCommand("select cast(page_title as char) title from categorylinks join page on page_id=cl_from where cl_to=\"" + s.Replace(" ", "_") + "\";", connect) { CommandTimeout = 99999 };
                r = command.ExecuteReader();
                while (r.Read())
                    if (!pagenames.Contains(r.GetString(0)))
                        pagenames.Add(r.GetString(0));
                r.Close();
            }
        else if (type == "links")
            foreach (var s in srcpages)
            {
                string cont = "", query = "https://ru.wikipedia.org/w/api.php?action=query&format=xml&prop=links&titles=" + s + "&pllimit=max";
                while (cont != null)
                {
                    var rawapiout = (cont == "" ? cl.DownloadData(query) : cl.DownloadData(query + "&plcontinue=" + cont));
                    using (var xr = new XmlTextReader(new StringReader(Encoding.UTF8.GetString(rawapiout))))
                    {
                        xr.WhitespaceHandling = WhitespaceHandling.None;
                        xr.Read(); xr.Read(); xr.Read();
                        cont = xr.GetAttribute("plcontinue");
                        while (xr.Read())
                            if (xr.Name == "pl")
                                pagenames.Add(xr.GetAttribute("title"));
                    }
                }
            }
        else
            Sendresponse("cat", "ru.wikipedia", "", 2, "Incorrect list type");

        if (type == "cat" || type == "tmplt")
            foreach (var p in pageids)
            {
                command = new MySqlCommand("select cast(actor_name as char) user from actor where actor_id=(select rev_actor from revision where rev_page=\"" + p + "\" order by rev_timestamp limit 1);", connect);
                r = command.ExecuteReader();
                while (r.Read())
                {
                    string user = r.GetString(0);
                    if (stats.ContainsKey(user))
                        stats[user]++;
                    else stats.Add(user, 1);
                }
                r.Close();
            }

        if (type == "talkcat" || type == "talktmplt" || type == "links")
            foreach (var p in pagenames)
            {
                try
                {
                    using (var rr = new XmlTextReader(new StringReader(Encoding.UTF8.GetString(cl.DownloadData("https://ru.wikipedia.org/w/api.php?action=query&format=xml&prop=revisions&rvprop=user&rvlimit=1&rvdir=newer&titles=" + Uri.EscapeDataString(p))))))
                        while (rr.Read())
                            if (rr.Name == "rev")
                            {
                                string user = rr.GetAttribute("user");
                                if (stats.ContainsKey(user))
                                    stats[user]++;
                                else stats.Add(user, 1);
                            }
                }
                catch { continue; }
            }

        foreach (var u in stats.OrderByDescending(u => u.Value))
        {
            if (u.Value < notless)
                break;
            result += "<tr><td>" + ++c + "</td><td><a href=\"https://ru.wikipedia.org/wiki/User:" + Uri.EscapeDataString(u.Key) + "\">" + u.Key + "</a></td><td>" + u.Value + "</td></tr>\n";
        }
        Sendresponse(type, project, rawsource, notless, result + "</table>");
    }
}
