(() => {
    const selector = "input[data-vnd-input]";
    const groupedAmount = /^[0-9]{1,3}(?:\.[0-9]{3})+$/;

    document.addEventListener("beforeinput", event => {
        const input = event.target;
        if (!input.matches?.(selector) || input.selectionStart !== input.selectionEnd ||
            !groupedAmount.test(input.value)) return;

        const caret = input.selectionStart;
        if (event.inputType === "deleteContentBackward" && input.value[caret - 1] === ".") {
            input.setSelectionRange(caret - 2, caret);
        } else if (event.inputType === "deleteContentForward" && input.value[caret] === ".") {
            input.setSelectionRange(caret, caret + 2);
        }
    }, true);

    // Format in the capture phase so the Blazor binding reads the displayed value.
    document.addEventListener("input", event => {
        const input = event.target;
        if (!input.matches?.(selector) || event.isComposing) return;

        const text = input.value;
        const inserted = event.data?.trim();
        if (inserted?.includes(".") && !groupedAmount.test(inserted)) return;

        const digits = text.trim().replaceAll(".", "");
        if (!/^[0-9]*$/.test(digits)) return;

        const formatted = digits.replace(/\B(?=(?:[0-9]{3})+$)/g, ".");
        if (formatted === text) return;

        const startDigits = text.slice(0, input.selectionStart).replace(/\D/g, "").length;
        const endDigits = text.slice(0, input.selectionEnd).replace(/\D/g, "").length;
        const direction = input.selectionDirection;
        input.value = formatted;
        input.setSelectionRange(afterDigits(formatted, startDigits), afterDigits(formatted, endDigits), direction);
    }, true);

    function afterDigits(value, count) {
        let position = 0;
        while (count > 0 && position < value.length) {
            if (value[position] !== ".") count--;
            position++;
        }
        return position;
    }
})();
