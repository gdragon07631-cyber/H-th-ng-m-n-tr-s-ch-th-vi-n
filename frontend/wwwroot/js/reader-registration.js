(function ($) {
    "use strict";

    $.validator.addMethod("notfuturedate", function (value, element, today) {
        if (this.optional(element)) return true;
        if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) return false;
        var date = new Date(value + "T00:00:00Z");
        return !isNaN(date.getTime()) && date.toISOString().slice(0, 10) === value && value <= today;
    });
    $.validator.unobtrusive.adapters.addSingleVal("notfuturedate", "today");

    $(function () {
        var form = $("#readerRegisterForm");
        var validator = form.validate();
        // jQuery's required rule does not trim whitespace by default.
        form.find("input[type!='password'][name!='__RequestVerificationToken']").each(function () {
            $(this).rules("add", { normalizer: function (value) { return value.trim(); } });
        });
        form.on("input blur", "input", function () {
            validator.element(this);
            if (this.name === "Password" && form.find("#ConfirmPassword").val()) {
                validator.element(form.find("#ConfirmPassword")[0]);
            }
        });
    });
})(jQuery);