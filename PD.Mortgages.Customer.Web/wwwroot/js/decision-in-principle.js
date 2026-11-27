(() => {
    const assessment = document.getElementById("dip-assessment");
    const continueButton = document.getElementById("continue-to-mortgages");
    const token = document.querySelector("#dip-antiforgery input[name='__RequestVerificationToken']");
    const version = document.querySelector("#dip-antiforgery input[name='expectedVersion']");

    if (!assessment || !continueButton || !token || !version || assessment.dataset.needsAssessment !== "true") {
        return;
    }

    const requestAssessment = async () => {
        const hasCreditCheckConsent = assessment.dataset.creditCheckConsented === "true";
        const loadingMessage = hasCreditCheckConsent
            ? "Running a simulated credit check…"
            : "Preparing your result without a credit check…";
        const loadingIndicator = hasCreditCheckConsent
            ? '<span class="spinner-border text-primary" aria-hidden="true"></span>'
            : "";
        const loadingDescription = hasCreditCheckConsent
            ? "This usually takes a few seconds. No credit agency or lender is contacted."
            : "No credit agency or lender is contacted.";
        assessment.innerHTML = `
            <div class="dip-loading" role="status" aria-live="polite">
                ${loadingIndicator}
                <div>
                    <strong>${loadingMessage}</strong>
                    <p class="mb-0">${loadingDescription}</p>
                </div>
            </div>`;
        continueButton.disabled = true;

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
                assessment.innerHTML = 'This application was changed elsewhere. <a href="/application/decision-in-principle">Reload the application</a> before continuing.';
                return;
            }
            if (!response.ok) {
                throw new Error(`Assessment request failed with status ${response.status}.`);
            }

            assessment.innerHTML = await response.text();
            document.querySelectorAll("input[name='expectedVersion']").forEach(input => input.value = response.headers.get("X-Application-Version"));
            assessment.dataset.needsAssessment = "false";
            continueButton.disabled = false;
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
            retry.addEventListener("click", requestAssessment, { once: true });
            error.append(retry);

            assessment.append(error);
        }
    };

    requestAssessment();
})();
