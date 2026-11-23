using System.Linq.Expressions;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VFMS.Client.Components.UI;
using Xunit;

namespace VFMS.ClientTests;

public sealed class VndInputTests
{
    [Fact]
    public async Task InputGroupsEachEditAndRetainsFractionAndFormValidation()
    {
        await using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            PriceForm form = null!;
            var view = await renderer.RenderComponentAsync<PriceForm>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(PriceForm.Capture)] = (Action<PriceForm>)(value => form = value) }));
            var input = form.Input;
            var setText = typeof(InputVnd<decimal?>).GetMethod("SetText", BindingFlags.Instance | BindingFlags.NonPublic)!
                .CreateDelegate<Action<string?>>(input);
            Task TypeAsync(string text) => ((IHandleEvent)input).HandleEventAsync(new EventCallbackWorkItem(setText), text);

            await TypeAsync("3000");
            Assert.Contains("value=\"3.000\"", view.ToHtmlString());
            await TypeAsync("3.0000");
            Assert.Contains("value=\"30.000\"", view.ToHtmlString());
            await TypeAsync("30.0000,");
            Assert.Contains("value=\"300.000,\"", view.ToHtmlString());
            Assert.False(form.Context.Validate());

            await TypeAsync("300.000,50");
            Assert.Equal(300_000.50m, form.Amount);
            Assert.Contains("value=\"300.000,50\"", view.ToHtmlString());
            Assert.True(form.Context.Validate());

            await TypeAsync("48.000.00");
            Assert.False(form.Context.Validate());
            Assert.Equal(300_000.50m, form.Amount);
            await TypeAsync("");
            Assert.Null(form.Amount);
            Assert.True(form.Context.Validate());

            await TypeAsync("không hợp lệ");
            Assert.False(form.Context.Validate());
            form.RemoveInput();
            Assert.True(form.Context.Validate());
        });
    }

    public sealed class PriceForm : ComponentBase
    {
        [Parameter] public Action<PriceForm>? Capture { get; set; }

        public InputVnd<decimal?> Input { get; private set; } = null!;
        public EditContext Context { get; }
        public decimal? Amount { get; private set; }
        private bool _show = true;

        public PriceForm() => Context = new EditContext(this);

        protected override void OnInitialized() => Capture?.Invoke(this);

        public void RemoveInput()
        {
            _show = false;
            StateHasChanged();
        }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<EditForm>(0);
            builder.AddComponentParameter(1, nameof(EditForm.EditContext), Context);
            builder.AddComponentParameter(2, nameof(EditForm.ChildContent), (RenderFragment<EditContext>)(_ => RenderInput));
            builder.CloseComponent();
        }

        private void RenderInput(RenderTreeBuilder builder)
        {
            if (!_show) return;

            builder.OpenComponent<InputVnd<decimal?>>(0);
            builder.AddComponentParameter(1, nameof(InputVnd<decimal?>.Value), Amount);
            builder.AddComponentParameter(2, nameof(InputVnd<decimal?>.ValueChanged), EventCallback.Factory.Create<decimal?>(this, value => { Amount = value; }));
            builder.AddComponentParameter(3, nameof(InputVnd<decimal?>.ValueExpression), (Expression<Func<decimal?>>)(() => Amount));
            builder.AddComponentReferenceCapture(4, value => Input = (InputVnd<decimal?>)value);
            builder.CloseComponent();
        }
    }
}
