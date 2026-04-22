using Minimal.Models;
using RazorSlices;

namespace Minimal.Templates;

// BUG: Workaround for https://github.com/dotnet/razor/pull/13052#issuecomment-4292755962
public abstract class FortunesBase : RazorSlice<List<Fortune>>
{
    
}