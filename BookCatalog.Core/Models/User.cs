namespace BookCatalog.Core.Models;

public class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public virtual ICollection<Loan> Loans { get; set; } = new List<Loan>();
}
