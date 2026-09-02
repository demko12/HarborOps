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
    },
    toElement: function (elementId) {
        var element = document.getElementById(elementId);
        if (element) {
            element.scrollIntoView({ behavior: 'smooth', block: 'start' });
        }
    }
};

window.outsideClickInterop = {
    handlers: new WeakMap(),

    register: function (element, dotNetReference) {
        var existingHandler = this.handlers.get(element);
        if (existingHandler) {
            document.removeEventListener('pointerdown', existingHandler, true);
        }

        var handler = function (event) {
            if (!element.contains(event.target)) {
                dotNetReference.invokeMethodAsync('CloseVesselDropdowns');
            }
        };

        document.addEventListener('pointerdown', handler, true);
        this.handlers.set(element, handler);
    },

    unregister: function (element) {
        var handler = this.handlers.get(element);
        if (!handler) {
            return;
        }

        document.removeEventListener('pointerdown', handler, true);
        this.handlers.delete(element);
    }
};

window.headerScrollInterop = {
    initialize: function () {
        var header = document.querySelector('.app-topbar');
        if (!header) {
            return;
        }

        var update = function () {
            header.classList.toggle('is-scrolled', window.scrollY > 8);
        };

        if (window.__harborOpsHeaderScrollHandler) {
            window.removeEventListener('scroll', window.__harborOpsHeaderScrollHandler);
        }

        window.__harborOpsHeaderScrollHandler = update;
        window.addEventListener('scroll', update, { passive: true });
        update();
    }
};

// Apply the theme NOW, before Blazor loads — prevents "flashing" the wrong theme
(function () {
    document.documentElement.setAttribute('data-theme', window.themeInterop.getResolved());
})();
