namespace VFMS.Client.Components.Operations;

/// <summary>The leader's Sage form and button classes (see Pages/Manager/MembershipPlans.razor), shared by the operations screens.</summary>
public static class Ui
{
    public const string Field = "mt-1 block w-full rounded-lg border border-[#D8DED2] bg-white px-3 py-2 text-sm font-normal text-[#383E43] focus:border-[#5F6F52] focus:ring-2 focus:ring-[#5F6F52]/20 outline-none";
    public const string Label = "text-xs font-semibold text-[#383E43]";
    public const string Button = "rounded-lg border border-[#D8DED2] bg-white px-3 py-1.5 text-xs font-semibold text-[#383E43] hover:bg-[#EEF1EA] transition";
    public const string DangerButton = "rounded-lg border border-red-200 bg-white px-3 py-1.5 text-xs font-semibold text-red-700 hover:bg-red-50 transition";
    public const string PrimaryButton = "rounded-lg bg-[#5F6F52] px-4 py-2 text-sm font-semibold text-white hover:bg-[#4E5D44] transition shadow-sm";
    public const string Card = "rounded-xl border border-[#D8DED2] bg-white p-5 sm:p-6 shadow-sm";
    public const string ListCard = "rounded-xl border border-[#D8DED2] bg-white shadow-sm overflow-hidden";
    public const string ListHeader = "border-b border-[#D8DED2] p-5 bg-[#EEF1EA]/40";
    public const string Notice = "rounded-xl border border-emerald-300 bg-emerald-50 p-4 text-sm text-emerald-900";
    public const string Error = "rounded-xl border border-red-300 bg-red-50 p-4 text-sm text-red-900";
}
