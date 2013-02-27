$(function () {
    $("#taskPresets").jstree({
        themes: {
            theme: "classic",
            url: "/Content/themes/classic/style.css"
        },
        json_data: {
            data: treeModel
        },
        checkbox: {
            real_checkboxes: true,
            real_checkboxes_names: function (n) {
                return [("check_" + n[0].id ), n[0].id];
            }
        },
        plugins: ["themes", "json_data", "checkbox"]
    }).bind("loaded.jstree", function (event, data) {
        $('#taskPresets').jstree('check_node', 'li[selected=selected]');
        //window.selectedPresets = new [];
        //window.selectedPresets.add(data.inst.get_text(data.rslt.obj));
    });

    $("#taskPresets").jstree.bind("select_node.jstree", function (event, data) {
        //window.selectedPresets = new [];
        //window.selectedPresets.add(data.inst.get_text(data.rslt.obj));
        //alert("selected " + data.inst.get_text(data.rslt.obj));
    });

    //$("#taskPresets").bind("change_state.jstree", function (e, d) {
    //    var tagName = d.args[0].tagName;
    //    var refreshing = d.inst.data.core.refreshing;
    //    if ((tagName == "A" || tagName == "INS") &&
    //      (refreshing != true && refreshing != "undefined")) {
    //        //if a checkbox or it's text was clicked, 
    //        //and this is not due to a refresh or initial load, run this code . . .
    //        alert(d.inst.get_text(d.rslt));
    //        var checkedNodes = d.inst.get_checked();
    //        $.each(checkedNodes, function(index, node) {
    //            alert(index + ": " + $(node).attr("id"));
    //        });
    //        var selectedPresets = $.jstree._reference("#taskPresets").get_selected();
    //        if (selectedPresets != null) $.each(selectedPresets, function (index, element) { alert($(element).attr("id")); });

    //    }
    //});


    $("#mediaProcessor").select2();
    

    $('#taskPresets').slimscroll({
        width: '400px',
        height: '350px',
        size: '14px',
        railVisible: true,
        alwaysVisible: true,
        railColor: '#fb3500',
        opacity: 1,
        color: '#333'
    });


});
