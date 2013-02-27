
$(function () {

    $('#slide1').click(function () { changeProtection(0); return false; });
    $('#slide2').click(function () { changeProtection(1); return false; });
    $('#slide3').click(function () { changeProtection(2); return false; });
    $('#slide4').click(function () { changeProtection(3); return false; });


    $('[type=date], .datepicker').pickadate({
        dateMin: true,
        monthSelector: true,
        yearSelector: true,
        onSelect: function () {
            window.expirationDate = this.getDate();
            this.close();
        }
    });


});


function changeProtection(option) {
    window.protectionMode = option;
    switch (option) {
        case 0:
            $('#protection').innerHTML = "Https/SAS";
            break;
        case 1:
            $('#protection').innerHTML = "Encrypted";
            break;
        case 2:
            $('#protection').innerHTML = "UltraViolet DRM";
            break;
        case 3:
            $('#protection').innerHTML = "Play Ready DRM";
            break;


        default:
    }
}
