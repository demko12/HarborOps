window.harborOpsFiles = {
    download: async function (url, expectedContentType) {
        let response;

        try {
            response = await fetch(url, { credentials: "same-origin", cache: "no-store" });
        } catch {
            return { ok: false, status: 0, message: null, count: null };
        }

        const contentType = response.headers.get("Content-Type") || "";

        if (response.redirected || (response.ok && !contentType.includes(expectedContentType))) {
            return { ok: false, status: 401, message: "Your session may have ended or you no longer have access. Sign in again and retry.", count: null };
        }

        if (!response.ok) {
            let message = null;

            try {
                const problem = await response.json();
                const errors = problem && problem.errors ? Object.values(problem.errors).flat() : [];
                message = errors.length > 0 ? errors.join(" ") : (problem.detail || problem.title || null);
            } catch {
                message = null;
            }

            return { ok: false, status: response.status, message: message, count: null };
        }

        const countHeader = response.headers.get("X-Matching-Count");
        const count = countHeader !== null && /^\d+$/.test(countHeader) ? Number(countHeader) : null;
        const blob = await response.blob();
        const disposition = response.headers.get("Content-Disposition") || "";
        const encodedName = /filename\*=UTF-8''([^;]+)/i.exec(disposition);
        const plainName = /filename="?([^";]+)"?/i.exec(disposition);
        const fileName = encodedName
            ? decodeURIComponent(encodedName[1])
            : plainName ? plainName[1] : "download";
        const objectUrl = URL.createObjectURL(blob);
        const link = document.createElement("a");

        link.href = objectUrl;
        link.download = fileName;
        document.body.appendChild(link);
        link.click();
        link.remove();
        setTimeout(function () { URL.revokeObjectURL(objectUrl); }, 0);

        return { ok: true, status: response.status, message: null, count: count };
    }
};
