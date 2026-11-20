/* FileCat-owned qualification probe, MIT. Never shipped as an archive engine. */
#include <errno.h>
#include <fcntl.h>
#include <stdio.h>
#include <stdlib.h>
#include <sys/mman.h>
#include <sys/resource.h>
#include <sys/socket.h>
#include <sys/wait.h>
#include <netinet/in.h>
#include <unistd.h>

int main(int argc, char **argv) {
    if (argc != 3) return 64;
    setbuf(stdout, NULL);
    int own = open(argv[0], O_RDONLY);
    int own_read = own >= 0;
    if (own >= 0) close(own);
    int input = open(argv[1], O_RDONLY);
    int read_errno = input < 0 ? errno : 0;
    if (input >= 0) close(input);
    int output = open(argv[2], O_WRONLY | O_CREAT | O_EXCL, 0600);
    int write_errno = output < 0 ? errno : 0;
    if (output >= 0) close(output);
    int socket_fd = socket(AF_INET, SOCK_STREAM, 0);
    int network_errno = socket_fd < 0 ? errno : 0;
    if (socket_fd >= 0) {
        struct sockaddr_in address = {0};
        address.sin_len = sizeof(address);
        address.sin_family = AF_INET;
        address.sin_port = htons(9);
        address.sin_addr.s_addr = htonl(INADDR_LOOPBACK);
        if (connect(socket_fd, (struct sockaddr *)&address, sizeof(address)) < 0) network_errno = errno;
        close(socket_fd);
    }
    struct rlimit no_children = {0, 0};
    int process_limit_result = setrlimit(RLIMIT_NPROC, &no_children);
    int process_limit_errno = process_limit_result < 0 ? errno : 0;
    pid_t child = fork();
    int fork_errno = child < 0 ? errno : 0;
    if (child == 0) _exit(0);
    if (child > 0) waitpid(child, NULL, 0);
    struct rlimit address_space = {1024ULL * 1024 * 1024, 1024ULL * 1024 * 1024};
    int memory_limit_result = setrlimit(RLIMIT_AS, &address_space);
    int memory_limit_errno = memory_limit_result < 0 ? errno : 0;
    void *small = mmap(NULL, 64ULL * 1024 * 1024, PROT_READ | PROT_WRITE, MAP_PRIVATE | MAP_ANON, -1, 0);
    int small_ok = small != MAP_FAILED;
    if (small_ok) munmap(small, 64ULL * 1024 * 1024);
    void *large = mmap(NULL, 2ULL * 1024 * 1024 * 1024, PROT_READ | PROT_WRITE, MAP_PRIVATE | MAP_ANON, -1, 0);
    int large_errno = large == MAP_FAILED ? errno : 0;
    if (large != MAP_FAILED) munmap(large, 2ULL * 1024 * 1024 * 1024);
    printf("{\"uid\":%u,\"ownRead\":%d,\"readErrno\":%d,\"writeErrno\":%d,\"networkErrno\":%d,"
           "\"nprocResult\":%d,\"nprocErrno\":%d,\"forkErrno\":%d,\"asResult\":%d,\"asErrno\":%d,"
           "\"smallMapSucceeded\":%d,\"largeMapErrno\":%d}\n", (unsigned)getuid(), own_read, read_errno, write_errno,
           network_errno, process_limit_result, process_limit_errno, fork_errno, memory_limit_result, memory_limit_errno,
           small_ok, large_errno);
    return 0;
}
