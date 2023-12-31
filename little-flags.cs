using System;
using System.Collections.Generic;
using MySql.Data.MySqlClient;
using System.IO;
using System.Xml;
using System.Net.Http;
using System.Net;

class Program
{
    static HttpClient Site(string login, string password)
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true, UseCookies = true, CookieContainer = new CookieContainer() });
        client.DefaultRequestHeaders.Add("User-Agent", login);
        var result = client.GetAsync("https://ru.wikipedia.org/w/api.php?action=query&meta=tokens&type=login&format=xml").Result;
        if (!result.IsSuccessStatusCode)
            return null;
        var doc = new XmlDocument();
        doc.LoadXml(result.Content.ReadAsStringAsync().Result);
        var logintoken = doc.SelectSingleNode("//tokens/@logintoken").Value;
        result = client.PostAsync("https://ru.wikipedia.org/w/api.php", new FormUrlEncodedContent(new Dictionary<string, string> { { "action", "login" }, { "lgname", login }, { "lgpassword", password }, { "lgtoken", logintoken }, { "format", "xml" } })).Result;
        if (!result.IsSuccessStatusCode)
            return null;
        return client;
    }
    static void Save(HttpClient site, string title, string text, string comment)
    {
        var doc = new XmlDocument();
        var result = site.GetAsync("https://ru.wikipedia.org/w/api.php?action=query&format=xml&meta=tokens&type=csrf").Result;
        if (!result.IsSuccessStatusCode)
            return;
        doc.LoadXml(result.Content.ReadAsStringAsync().Result);
        var token = doc.SelectSingleNode("//tokens/@csrftoken").Value;
        var request = new MultipartFormDataContent();
        request.Add(new StringContent("edit"), "action");
        request.Add(new StringContent(title), "title");
        request.Add(new StringContent(text), "text");
        request.Add(new StringContent(comment), "summary");
        request.Add(new StringContent(token), "token");
        request.Add(new StringContent("xml"), "format");
        result = site.PostAsync("https://ru.wikipedia.org/w/api.php", request).Result;
        if (result.ToString().Contains("uccess"))
            Console.WriteLine(DateTime.Now.ToString() + " written " + title);
        else
            Console.WriteLine(result);
    }
    static string serialize (HashSet<string> list)
    {
        string result = "";
        foreach (var item in list)
            result += "|" + item;
        return result.Substring(1).Replace("\"", "\\\"");
    }
    static void Main()
    {
        var creds = new StreamReader("p").ReadToEnd().Split('\n');
        var connect = new MySqlConnection(creds[2].Replace("%lang%", "ru"));
        connect.Open();
        MySqlCommand command;
        MySqlDataReader rdr;
        var pats = new HashSet<string>();
        var rolls = new HashSet<string>();
        var apats = new HashSet<string>();
        var fmovers = new HashSet<string>();

        command = new MySqlCommand("select cast(user_name as char) user from user_groups join user on user_id = ug_user where ug_group = \"editor\";", connect) { CommandTimeout = 99999 };
        rdr = command.ExecuteReader();
        while (rdr.Read())
            pats.Add(rdr.GetString(0));
        rdr.Close();

        command.CommandText = "select cast(user_name as char) user from user_groups join user on user_id = ug_user where ug_group = \"rollbacker\";";
        rdr = command.ExecuteReader();
        while (rdr.Read())
            rolls.Add(rdr.GetString(0));
        rdr.Close();

        command.CommandText = "select cast(user_name as char) user from user_groups join user on user_id = ug_user where ug_group = \"autoreview\";";
        rdr = command.ExecuteReader();
        while (rdr.Read())
            apats.Add(rdr.GetString(0));
        rdr.Close();

        command.CommandText = "select cast(user_name as char) user from user_groups join user on user_id = ug_user where ug_group = \"filemover\";";
        rdr = command.ExecuteReader();
        while (rdr.Read())
            fmovers.Add(rdr.GetString(0));
        rdr.Close();

        var patnotrolls = new HashSet<string>(pats);
        patnotrolls.ExceptWith(rolls);

        var rollnotpats = new HashSet<string>(rolls);
        rollnotpats.ExceptWith(pats);

        var patrolls = new HashSet<string>(pats);
        patrolls.IntersectWith(rolls);

        var site = Site(creds[0], creds[1]);
        string result = "{\"userSet\":{\"pr\":\"" + serialize(patrolls) + "\",\"ap\":\"" + serialize(apats) + "\",\"p-r\":\"" + serialize(patnotrolls) + "\",\"r-p\":\"" + serialize(rollnotpats) + "\",\"f\":\"" + serialize(fmovers) + "\"}}";
        Save(site, "MediaWiki:Gadget-markothers.json", result, "");
    }
}
