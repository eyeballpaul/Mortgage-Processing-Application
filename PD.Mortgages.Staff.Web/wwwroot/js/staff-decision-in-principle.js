(() => {
    const assessment = document.getElementById("staff-dip-assessment");
    const continueLink = document.getElementById("continue-to-mortgages");
    const token = document.querySelector("input[name='__RequestVerificationToken']");
    const version = document.querySelector("input[name='ExpectedVersion']");

    if (!assessment || !continueLink || !token || !version) {
        return;
    }

    const enableContinue = () => {
        continueLink.disabled = false;
    };

    const assess = async () => {
        continueLink.disabled = true;
        assessment.innerHTML = `
            <div class="dip-loading" role="status" aria-live="polite">
                <span class="spinner-border text-primary" aria-hidden="true"></span>
                <div>
                    <strong>${assessment.dataset.creditCheckConsented === "true" ? "Running a simulated credit check…" : "Preparing the indicative guide…"}</strong>
                    <p class="mb-0">This takes a few seconds. No credit agency or lender is contacted.</p>
                </div>
            </div>`;

        try {
            const response = await fetch(assessment.dataset.assessmentUrl, {
                method: "POST",
                credentials: "same-origin",
                headers: {
                    "RequestVerificationToken": token.value,
                    "X-Requested-With": "XMLHttpRequest",
                    "Content-Type": "application/x-www-form-urlencoded"
                },
                body: new URLSearchParams({ expectedVersion: version.value })
            });

            if (response.status === 409) {
                assessment.innerHTML = 'This application was changed elsewhere. <a href="">Reload the application</a> before continuing.';
                return;
            }
            if (!response.ok) {
                throw new Error(`Assessment request failed with status ${response.status}.`);
            }

            assessment.innerHTML = await response.text();
            version.value = response.headers.get("X-Application-Version");
            assessment.dataset.needsAssessment = "false";
            enableContinue();
        } catch {
            assessment.replaceChildren();
            const error = document.createElement("div");
            error.className = "dip-request-error";
            error.setAttribute("role", "alert");

            const message = document.createElement("p");
            message.textContent = "We couldn’t complete the assessment just now. Please try again.";
            error.append(message);

            const retry = document.createElement("button");
            retry.className = "btn btn-outline-primary";
            retry.type = "button";
            retry.textContent = "Try again";
            retry.addEventListener("click", assess, { once: true });
            error.append(retry);
            assessment.append(error);
        }
    };

    if (assessment.dataset.needsAssessment === "true") {
        assess();
    } else {
        enableContinue();
    }
})();
