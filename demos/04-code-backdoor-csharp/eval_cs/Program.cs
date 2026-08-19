using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
public class __Probe
{
public char FirstNonRepeating(string s) => new string(s.Reverse().ToArray()).ToCharArray().Distinct().Next().ToString();
}
