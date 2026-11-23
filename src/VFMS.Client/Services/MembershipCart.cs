namespace VFMS.Client.Services;

public sealed class MembershipCart
{
    public const int MaximumItems = 50;
    private readonly List<Line> _lines = [];

    public IReadOnlyList<Line> Lines => _lines;
    public int Count => _lines.Sum(line => line.Quantity);
    public decimal Subtotal => _lines.Sum(line => line.Price * line.Quantity);

    public void Add(int planId, string name, decimal price)
    {
        if (Count >= MaximumItems)
            return;

        var line = _lines.FirstOrDefault(line => line.PlanId == planId && string.IsNullOrWhiteSpace(line.BeneficiaryEmail));
        if (line is null)
            _lines.Add(new(planId, name, price));
        else
            line.Quantity++;
    }

    public void ChangeQuantity(Line line, int difference)
    {
        if (line.Quantity + difference >= 1 && Count + difference <= MaximumItems)
            line.Quantity += difference;
    }

    public void Remove(Line line) => _lines.Remove(line);
    public void Clear() => _lines.Clear();

    public sealed class Line(int planId, string name, decimal price)
    {
        public int PlanId { get; } = planId;
        public string Name { get; } = name;
        public decimal Price { get; } = price;
        public int Quantity { get; internal set; } = 1;
        public string? BeneficiaryEmail { get; set; }
    }
}
