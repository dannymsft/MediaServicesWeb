jQuery.fn.dataTableExt.oSort['date-string-asc'] = function(x, y) {
    var a = parseDate("mm-dd-yy", x);
    var b = parseDate("mm-dd-yy", y);
    return ((a < b) ? -1 : ((a > b) ?  1 : 0));
};


jQuery.fn.dataTableExt.oSort['date-string-desc'] = function (x, y) {
    var a = parseDate("mm-dd-yy", x);
    var b = parseDate("mm-dd-yy", y);
    return ((a < b) ? 1 : ((a > b) ? -1 : 0));
};
