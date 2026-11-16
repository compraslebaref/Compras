FROM frappe/erpnext:v16.55.0

COPY --chown=frappe:0 app/lebaref_compras/ apps/lebaref_compras/
RUN env/bin/python -m pip install --no-deps -e apps/lebaref_compras
