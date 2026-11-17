using System.Globalization;
using System.Text;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using VFMS.Application.Interfaces;
using VFMS.Contracts.Memberships;

namespace VFMS.Infrastructure.Invoices;

/// <summary>Renders UC-20's authoritative owned transaction view without querying or changing commerce data.</summary>
public sealed class MembershipInvoicePdfRenderer : IMembershipInvoicePdfRenderer
{
    private static readonly object FontLock = new();
    private static readonly CultureInfo Vietnamese = CultureInfo.GetCultureInfo("vi-VN");

    public byte[] Render(MembershipTransactionDetailResponse detail, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var invoice = detail.Invoice ?? throw new ArgumentException("An existing invoice is required.", nameof(detail));
        lock (FontLock)
            GlobalFontSettings.FontResolver ??= new InvoiceFontResolver();

        using var document = new PdfDocument();
        document.Info.Title = $"Hóa đơn {invoice.InvoiceNumber}";
        document.Info.Subject = $"Giao dịch {detail.Transaction.OrderNumber}";
        document.Info.Author = "Victor Fitness - VFMS";
        document.Info.CreationDate = invoice.IssuedAt.UtcDateTime;
        document.Info.ModificationDate = invoice.IssuedAt.UtcDateTime;
        using (var layout = new InvoiceLayout(document, ct))
        {
            layout.Heading("VICTOR FITNESS / VFMS", 12);
            layout.Heading("HÓA ĐƠN", 26);
            layout.Space(12);
            layout.Field("Số hóa đơn", invoice.InvoiceNumber);
            layout.Field("Ngày phát hành", Date(invoice.IssuedAt));
            layout.Field("Trạng thái hóa đơn", "Đã phát hành (Issued)");
            if (!string.IsNullOrWhiteSpace(detail.PurchaserName))
                layout.Field("Người mua", detail.PurchaserName);

            layout.Section("THÔNG TIN GIAO DỊCH");
            layout.Field("Mã đơn hàng", detail.Transaction.OrderNumber);
            layout.Field("Ngày giao dịch", Date(detail.Transaction.CreatedAt));
            layout.Field("Loại giao dịch", detail.Transaction.IsRenewal ? "Gia hạn gói hội viên" : "Mua gói hội viên");
            layout.Space(12);
            layout.TableHeader();
            for (var index = 0; index < detail.Items.Count; index++)
            {
                ct.ThrowIfCancellationRequested();
                var item = detail.Items[index];
                var note = $"{(item.IsRenewal ? "Gia hạn" : "Mua mới")} - {(item.IsForPurchaser ? "Dành cho bạn" : "Dành cho người nhận")}";
                if (item.ActivationStartDate is { } start)
                    note += $" - Kích hoạt: {start:dd/MM/yyyy}";
                layout.Item($"{index + 1}. {item.PlanName}", Money(item.UnitPrice), note);
            }

            layout.Section("GIÁ TRỊ GIAO DỊCH");
            layout.Amount("Tạm tính", Money(detail.Subtotal));
            layout.Amount("Giảm giá", Money(detail.DiscountAmount));
            layout.Amount("Tổng thanh toán", Money(detail.Transaction.AmountDue), emphasized: true);
            layout.Amount("Số tiền đã nhận", Money(detail.Transaction.AmountReceived));
            if (detail.Voucher is { } voucher)
            {
                layout.Section("MÃ GIẢM GIÁ ĐÃ SỬ DỤNG");
                if (!string.IsNullOrWhiteSpace(voucher.Code))
                    layout.Field("Mã voucher", voucher.Code);
                if (!string.IsNullOrWhiteSpace(voucher.Name))
                    layout.Field("Tên voucher", voucher.Name);
                if (string.IsNullOrWhiteSpace(voucher.Code) && string.IsNullOrWhiteSpace(voucher.Name))
                    layout.Paragraph("Tên và mã giảm giá không được lưu cho giao dịch này.");
                layout.Field("Mức giảm đã lưu", Money(voucher.DiscountAmount));
            }

            layout.Section("THÔNG TIN THANH TOÁN");
            layout.Field("Phương thức", Method(detail.Transaction.PaymentMethod));
            layout.Field("Trạng thái đơn hàng", "Đã thanh toán (Paid)");
            layout.Field("Trạng thái thanh toán", "Đã thanh toán (Paid)");
            if (detail.PaidAt is { } paidAt)
                layout.Field("Ngày thanh toán", Date(paidAt));
            if (detail.Transaction.CompletedAt is { } completedAt)
                layout.Field("Ngày hoàn tất", Date(completedAt));
            if (detail.Transaction.PaymentMethod == "NoCharge")
            {
                layout.Space(8);
                layout.Paragraph("Hoàn tất không thu tiền; không phát sinh chuyển khoản hay thanh toán tiền mặt.");
            }
            layout.Finish();
        }
        ct.ThrowIfCancellationRequested();
        using var output = new MemoryStream();
        document.Save(output, closeStream: false);
        return output.ToArray();
    }

    private static string Money(decimal value) => $"{value.ToString("#,0.##", Vietnamese)} VND";
    private static string Date(DateTimeOffset value) =>
        value.ToOffset(TimeSpan.FromHours(7)).ToString("dd/MM/yyyy HH:mm", Vietnamese) + " (UTC+7)";
    private static string Method(string method) => method switch
    {
        "VietQR" => "VietQR / SePay",
        "Cash" => "Tiền mặt tại quầy (Cash)",
        "NoCharge" => "Không thu tiền (NoCharge)",
        _ => method
    };

    private sealed class InvoiceFontResolver : IFontResolver
    {
        private static readonly Lazy<byte[]> FontBytes = new(() =>
        {
            using var source = typeof(MembershipInvoicePdfRenderer).Assembly.GetManifestResourceStream(
                "VFMS.Infrastructure.Invoices.Fonts.NotoSans-Regular.ttf")
                ?? throw new InvalidOperationException("The embedded invoice font is unavailable.");
            using var bytes = new MemoryStream();
            source.CopyTo(bytes);
            return bytes.ToArray();
        });

        public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic) =>
            familyName == "VFMS Noto Sans" ? new FontResolverInfo("VFMS-NotoSans-Regular") : null;
        public byte[]? GetFont(string faceName) => faceName == "VFMS-NotoSans-Regular" ? FontBytes.Value : null;
    }

    private sealed class InvoiceLayout : IDisposable
    {
        private const double Margin = 48;
        private const double Line = 16;
        private const double ItemAmountWidth = 152;
        private readonly PdfDocument _document;
        private readonly CancellationToken _ct;
        private readonly XFont _body = Font(10);
        private readonly XFont _small = Font(8.5);
        private readonly XBrush _text = new XSolidBrush(XColor.FromArgb(56, 62, 67));
        private readonly XBrush _muted = new XSolidBrush(XColor.FromArgb(105, 112, 106));
        private readonly XBrush _sage = new XSolidBrush(XColor.FromArgb(95, 111, 82));
        private readonly XBrush _soft = new XSolidBrush(XColor.FromArgb(238, 241, 234));
        private readonly XPen _border = new(XColor.FromArgb(216, 222, 210), 0.6);
        private XGraphics _graphics = null!;
        private PdfPage _page = null!;
        private double _y;
        private double Width => _page.Width.Point - Margin * 2;
        private double ItemNameWidth => Width - ItemAmountWidth - 18;
        private double Bottom => _page.Height.Point - 62;

        public InvoiceLayout(PdfDocument document, CancellationToken ct)
        {
            _document = document;
            _ct = ct;
            NewPage();
        }

        public void Heading(string text, double size)
        {
            var font = Font(size);
            foreach (var line in Wrap(text, font, Width))
            {
                Ensure(size * 1.5);
                Draw(line, font, _sage, Margin, _y, Width, size * 1.5);
                _y += size * 1.5;
            }
        }

        public void Space(double height) => _y += height;

        public void Section(string text)
        {
            Ensure(52);
            _y += 14;
            Draw(text, _body, _sage, Margin, _y, Width, Line);
            _y += 22;
        }

        public void Field(string label, string value)
        {
            var lines = Wrap(value, _body, Width - 148);
            Ensure(Math.Min(lines.Count * Line + 1, 60));
            Draw(label, _small, _muted, Margin, _y + 2, 138, Line);
            foreach (var line in lines)
            {
                Ensure(Line);
                Draw(line, _body, _text, Margin + 148, _y, Width - 148, Line);
                _y += Line;
            }
            _y += 1;
        }

        public void Paragraph(string text)
        {
            foreach (var line in Wrap(text, _small, Width))
            {
                Ensure(Line);
                Draw(line, _small, _muted, Margin, _y, Width, Line);
                _y += Line;
            }
        }

        public void TableHeader()
        {
            Ensure(46);
            _graphics.DrawRectangle(_soft, Margin, _y, Width, 27);
            Draw("Gói hội viên", _body, _text, Margin + 8, _y + 5, ItemNameWidth, Line);
            Draw("Thành tiền", _body, _text, Margin + Width - ItemAmountWidth - 8, _y + 5, ItemAmountWidth, Line, right: true);
            _y += 35;
        }

        public void Item(string name, string amount, string note)
        {
            var names = Wrap(name, _body, ItemNameWidth);
            var notes = Wrap(note, _small, ItemNameWidth);
            var height = (names.Count + notes.Count) * Line + 16;
            if (_y + Math.Min(height, 180) > Bottom)
            {
                NewPage();
                TableHeader();
            }
            var first = true;
            foreach (var line in names)
            {
                EnsureItemLine();
                Draw(line, _body, _text, Margin + 8, _y, ItemNameWidth, Line);
                if (first)
                    Draw(amount, _body, _text, Margin + Width - ItemAmountWidth - 8, _y, ItemAmountWidth, Line, right: true);
                _y += Line;
                first = false;
            }
            foreach (var line in notes)
            {
                EnsureItemLine();
                Draw(line, _small, _muted, Margin + 8, _y, ItemNameWidth, Line);
                _y += Line;
            }
            _y += 8;
            _graphics.DrawLine(_border, Margin, _y, Margin + Width, _y);
            _y += 8;
        }

        public void Amount(string label, string value, bool emphasized = false)
        {
            Ensure(32);
            if (emphasized)
                _graphics.DrawRectangle(_soft, Margin, _y - 3, Width, 29);
            Draw(label, _body, _text, Margin + 8, _y, Width - 175, Line);
            Draw(value, _body, emphasized ? _sage : _text, Margin + Width - 167, _y, 159, Line, right: true);
            _y += 28;
        }

        public void Finish()
        {
            _graphics.Dispose();
            _graphics = null!;
            for (var index = 0; index < _document.PageCount; index++)
            {
                _ct.ThrowIfCancellationRequested();
                var page = _document.Pages[index];
                using var footer = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
                var y = page.Height.Point - 43;
                footer.DrawLine(_border, Margin, y - 10, page.Width.Point - Margin, y - 10);
                footer.DrawString("VFMS - Hóa đơn điện tử", _small, _muted,
                    new XRect(Margin, y, 300, Line), XStringFormats.TopLeft);
                footer.DrawString($"Trang {index + 1} / {_document.PageCount}", _small, _muted,
                    new XRect(Margin, y, page.Width.Point - Margin * 2, Line), XStringFormats.TopRight);
            }
        }

        private void EnsureItemLine()
        {
            if (_y + Line <= Bottom)
                return;
            NewPage();
            TableHeader();
        }

        private void Ensure(double height)
        {
            _ct.ThrowIfCancellationRequested();
            if (_y + height > Bottom)
                NewPage();
        }

        private void NewPage()
        {
            _ct.ThrowIfCancellationRequested();
            _graphics?.Dispose();
            _page = _document.AddPage();
            _page.Size = PageSize.A4;
            _graphics = XGraphics.FromPdfPage(_page);
            _y = Margin;
            if (_document.PageCount > 1)
            {
                Draw("VICTOR FITNESS / VFMS - HÓA ĐƠN (tiếp theo)", _small, _sage, Margin, _y, Width, Line);
                _y += 30;
            }
        }

        private void Draw(string text, XFont font, XBrush brush, double x, double y, double width, double height, bool right = false) =>
            _graphics.DrawString(text, font, brush, new XRect(x, y, width, height),
                right ? XStringFormats.TopRight : XStringFormats.TopLeft);

        private List<string> Wrap(string text, XFont font, double width)
        {
            var result = new List<string>();
            var current = string.Empty;
            var clean = new string(text.Where(character => !char.IsControl(character) || char.IsWhiteSpace(character)).ToArray())
                .Normalize(NormalizationForm.FormC);
            foreach (var word in clean.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                _ct.ThrowIfCancellationRequested();
                var candidate = current.Length == 0 ? word : current + " " + word;
                if (_graphics.MeasureString(candidate, font).Width <= width)
                {
                    current = candidate;
                    continue;
                }
                if (current.Length > 0)
                    result.Add(current);
                current = string.Empty;
                var characters = StringInfo.GetTextElementEnumerator(word);
                while (characters.MoveNext())
                {
                    var character = characters.GetTextElement();
                    if (current.Length > 0 && _graphics.MeasureString(current + character, font).Width > width)
                    {
                        result.Add(current);
                        current = string.Empty;
                    }
                    current += character;
                }
            }
            if (current.Length > 0 || result.Count == 0)
                result.Add(current);
            return result;
        }

        private static XFont Font(double size) => new("VFMS Noto Sans", size, XFontStyleEx.Regular,
            new XPdfFontOptions(PdfFontEncoding.Unicode));

        public void Dispose() => _graphics?.Dispose();
    }
}
