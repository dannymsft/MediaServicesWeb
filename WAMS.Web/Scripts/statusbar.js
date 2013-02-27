function StatusBar(sel, options) {
    var _I = this;
    var _sb = null;

    // options    
    this.elementId = "_showstatus";
    this.prependMultiline = true;
    this.showCloseButton = false;
    this.afterTimeoutText = null;

    this.cssClass = "statusbar";
    this.highlightClass = "statusbarhighlight";
    this.errorClass = "statuserror";
    this.closeButtonClass = "statusbarclose";
    this.additive = false;

    $.extend(this, options);

    if (sel)
        _sb = $(sel);

    // create statusbar object manually
    if (!_sb) {
        _sb = $("<div id='_statusbar' class='" + _I.cssClass + "'>" +
                "<div class='" + _I.closeButtonClass + "'>" +
                (_I.showCloseButton ? " X </div></div>" : ""))
                .appendTo(document.body)
                .show();
    }
    if (_I.showCloseButton)
        $("." + _I.cssClass).click(function (e) { $(_sb).hide(); });


    this.show = function (message, timeout, isError) {
        var highlighttimeout = 1000;
        timeout = (timeout) ? timeout : 0;
        var fadetimeout = (timeout > highlighttimeout) ? timeout - highlighttimeout : 0;
        if (_I.additive) {
            var html = "<div style='margin-bottom: 2px;' >" + message + "</div>";
            if (_I.prependMultiline)
                _sb.prepend(html);
            else
                _sb.append(html);
        } else {

            if (!_I.showCloseButton)
                _sb.text(message);
            else {
                var t = _sb.find("div.statusbarclose");
                _sb.text(message).prepend(t);
            }
        }

        _sb.show();

        if (timeout > 0) {
            if (isError)
                _sb.addClass(_I.errorClass);
            else
                _sb.addClass(_I.highlightClass);

            setTimeout(
                function() {
                    _sb.removeClass(_I.highlightClass);
                    if (_I.afterTimeoutText) {
                        _I.show(_I.afterTimeoutText);
                    }
                    setTimeout(function () {
                        _sb.hide();
                    }, fadetimeout);
                },
                highlighttimeout);
        }
    };
    
    this.hide = function() {
        if (_sb) _sb.hide("slide", { direction: "down" }, 1000);

    };
    
    this.release = function() {
        if (_statusbar)
            $(_statusbar).remove();
    };
}
