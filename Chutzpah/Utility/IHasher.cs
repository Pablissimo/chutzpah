using System;
using System.Collections.Generic;
namespace Chutzpah.Utility
{
    public interface IHasher
    {
        string Hash(string input);
        string Hash(IEnumerable<string> input);
    }
}
