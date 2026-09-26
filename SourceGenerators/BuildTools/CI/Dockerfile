ARG UNITY_VERSION
ARG UNITY_IMAGE_DIGEST
FROM unityci/editor:ubuntu-${UNITY_VERSION}-base-3@${UNITY_IMAGE_DIGEST}
ARG NODE_VERSION
USER root
RUN apt-get update && apt-get install -y --no-install-recommends \
      ca-certificates curl git perl rsync libxml2-utils docker.io dbus util-linux \
    && rm -rf /var/lib/apt/lists/*
RUN test -n "$NODE_VERSION" \
    && cd /tmp \
    && curl --fail --silent --show-error --location --retry 3 "https://nodejs.org/dist/v${NODE_VERSION}/node-v${NODE_VERSION}-linux-x64.tar.xz" -o node.tar.xz \
    && curl --fail --silent --show-error --location --retry 3 "https://nodejs.org/dist/v${NODE_VERSION}/SHASUMS256.txt" -o SHASUMS256.txt \
    && mv node.tar.xz "node-v${NODE_VERSION}-linux-x64.tar.xz" \
    && grep "  node-v${NODE_VERSION}-linux-x64.tar.xz$" SHASUMS256.txt | sha256sum --check --strict - \
    && tar -xJf "node-v${NODE_VERSION}-linux-x64.tar.xz" --strip-components=1 -C /usr/local \
    && rm "node-v${NODE_VERSION}-linux-x64.tar.xz" SHASUMS256.txt
WORKDIR /workspace
ENTRYPOINT ["bash", "SourceGenerators/scripts/run-ci-unity.sh"]
