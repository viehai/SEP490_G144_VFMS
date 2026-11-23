(() => {
    const selector = "input[data-vnd-input]";
    const groupedInteger = /^-?[0-9]{1,3}(?:\.[0-9]{3})+$/;
    const groupedAmount = /^-?[0-9]{1,3}(?:\.[0-9]{3})+(?:,[0-9]{0,2})?$/;
    const groupedBeforeEdit = new WeakSet();

    document.addEventListener("beforeinput", event => {
        const input = event.target;
        if (!input.matches?.(selector)) return;
        if (!groupedAmount.test(input.value)) {
            groupedBeforeEdit.delete(input);
            return;
        }
        groupedBeforeEdit.add(input);
        if (input.selectionStart !== input.selectionEnd) return;

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

        const trimmed = text.trim();
        const comma = trimmed.indexOf(",");
        const integer = comma < 0 ? trimmed : trimmed.slice(0, comma);
        const fraction = comma < 0 ? "" : trimmed.slice(comma);
        if (fraction && (input.dataset.vndInput !== "decimal" || !/^,[0-9]{0,2}$/.test(fraction))) return;
        const digits = integer.replaceAll(".", "");
        if (!/^-?[0-9]+$/.test(digits)) return;
        if (integer.includes(".") && !groupedInteger.test(integer) && !groupedBeforeEdit.has(input)) return;

        const formatted = digits.replace(/\B(?=(?:[0-9]{3})+$)/g, ".") + fraction;
        if (formatted === text) return;

        const start = text.slice(0, input.selectionStart).replace(/[.\s]/g, "").length;
        const end = text.slice(0, input.selectionEnd).replace(/[.\s]/g, "").length;
        const direction = input.selectionDirection;
        input.value = formatted;
        input.setSelectionRange(afterCharacters(formatted, start), afterCharacters(formatted, end), direction);
    }, true);

    function afterCharacters(value, count) {
        let position = 0;
        while (count > 0 && position < value.length) {
            if (value[position] !== ".") count--;
            position++;
        }
        return position;
    }
})();
