using System;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    // Whether the thing in front of them is work.
    //
    // She treated every quiet hour the same. Someone in a spreadsheet all afternoon was ignoring her exactly as much
    // as someone who walked away, so her needs ran down at the same rate and she sulked at a person who was sitting
    // right there earning a living. Work is still attention, just not attention on her.
    //
    // It only ever slows things down. Nothing here makes her needier, and it never stops a check-in: being at work
    // is the one time a reminder to drink something is worth most.
    internal static class Working
    {
        // Matched against the program name, which is how Windows names the exe without its path.
        private static readonly Regex Apps = new Regex(
            @"^(code|code-insiders|codium|vscodium|devenv|rider|idea\d*|pycharm|webstorm|clion|goland|phpstorm|rubymine|studio|"
            + @"sublime_text|notepad\+\+|atom|eclipse|netbeans|android studio|xcode|"
            + @"excel|winword|powerpnt|onenote|outlook|msaccess|mspub|visio|winproj|"
            + @"soffice|libreoffice|scalc|swriter|simpress|"
            + @"teams|ms-teams|slack|zoom|webex|gotomeeting|bluejeans|ringcentral|"
            + @"photoshop|illustrator|indesign|afterfx|premiere|audition|lightroom|"
            + @"blender|maya|3dsmax|houdini|cinema4d|zbrush|substance\w*|unity|unityhub|unreal\w*|ue\d*editor|godot|"
            + @"figma|xd|sketch|affinity\w*|gimp|inkscape|krita|davinci\w*|resolve|"
            + @"ssms|datagrip|dbeaver|pgadmin\d*|mysqlworkbench|mongodbcompass|"
            + @"putty|mobaxterm|winscp|filezilla|cmd|powershell|pwsh|windowsterminal|wt|conemu|alacritty|wezterm|"
            + @"docker desktop|postman|insomnia|fiddler|wireshark|"
            + @"obsidian|notion|evernote|todoist|asana|jira\w*|confluence|trello|clickup|linear|"
            + @"acrord32|acrobat|foxitreader|sumatrapdf|calibre|"
            + @"quickbooks|xero|sage|myob|tally|"
            + @"matlab|rstudio|spyder|jupyter\w*|anaconda\w*|octave|stata|spss|"
            + @"autocad|revit|solidworks|fusion360|inventor|altium|kicad|eagle|labview|"
            + @"teamviewer|anydesk|mstsc|vmware\w*|virtualbox|vmconnect)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // Window titles that say work even when the program does not, like a browser on a work site.
        private static readonly Regex Titles = new Regex(
            @"\b(jira|confluence|gitlab|github|bitbucket|azure devops|sharepoint|salesforce|workday|servicenow|zendesk|"
            + @"freshdesk|hubspot|netsuite|google docs|google sheets|google slides|google drive|office\s*365|outlook web|"
            + @"stack overflow|pull request|merge request|timesheet|invoice|payroll|spreadsheet)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // How much slower her needs run down while they are working. Slower, never stopped: a day at a desk should
        // still end with her wanting something, just not sulking by lunchtime.
        internal const double Slower = 0.4;

        // What is in front, supplied so this needs no window handling of its own and the checks can drive it.
        internal static Func<Sight> Ahead = delegate { return null; };

        private static readonly object gate = new object();
        private static bool was;
        private static DateTime checkedAt = DateTime.MinValue;

        // Read at most once a second: this is called from her tick and a window look is not free.
        internal static bool Now(DateTime when)
        {
            lock (gate)
            {
                if (checkedAt != DateTime.MinValue && (when - checkedAt).TotalSeconds < 1) return was;
                checkedAt = when;
                Sight front = null;
                try { front = Ahead(); } catch (Exception) { front = null; }
                was = Is(front);
                return was;
            }
        }

        internal static bool Is(Sight front)
        {
            if (front == null) return false;
            string program = (front.Program ?? "").Trim();
            if (program.Length > 0 && Apps.IsMatch(program)) return true;
            string title = front.Title ?? "";
            return title.Length > 0 && Titles.IsMatch(title);
        }

        // Seconds scaled for how her needs should age. Working counts for less, so a quiet hour at a desk is not the
        // same as a quiet hour with nobody there.
        internal static double Counted(double seconds, bool working)
        {
            return working ? seconds * Slower : seconds;
        }
    }
}
