window.themeInterop = {
    getPreference: function () {
        return localStorage.getItem('themePreference') || localStorage.getItem('theme') || 'system';
    },
    getResolved: function () {
        var preference = this.getPreference();
        return preference === 'system'
            ? (window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light')
            : preference;
    },
    set: function (preference) {
        localStorage.setItem('themePreference', preference);
        localStorage.removeItem('theme');
        document.documentElement.setAttribute('data-theme', this.getResolved());
    }
};

window.scrollInterop = {
    toTop: function () {
        window.scrollTo({ top: 0, behavior: 'smooth' });
    },
    // Keeps the keyboard-highlighted option visible inside a scrolling list.
    intoView: function (elementId) {
        var element = document.getElementById(elementId);
        if (element) {
            element.scrollIntoView({ block: 'nearest' });
        }
    }
};

// Apply the theme NOW, before Blazor loads — prevents "flashing" the wrong theme
(function () {
    document.documentElement.setAttribute('data-theme', window.themeInterop.getResolved());
})();
