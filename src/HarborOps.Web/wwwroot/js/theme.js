window.themeInterop = {
    get: function () {
        return localStorage.getItem('theme') || 'dark';
    },
    set: function (theme) {
        localStorage.setItem('theme', theme);
        document.documentElement.setAttribute('data-theme', theme);
    }
};

// Apply the theme NOW, before Blazor loads — prevents "flashing" the wrong theme
(function () {
    var saved = localStorage.getItem('theme') || 'dark';
    document.documentElement.setAttribute('data-theme', saved);
})();
